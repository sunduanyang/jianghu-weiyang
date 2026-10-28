// Recover readable native control flow from the supplied IL2CPP executable.
// The metadata supplies symbols and candidate types, not executable C# bodies.
// @category BloodyCombat
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.program.model.address.*;
import ghidra.program.model.data.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.SourceType;
import com.google.gson.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

public class ExportCombat extends GhidraScript {
    private final Map<String,DataType> types = new HashMap<>();
    private final Map<String,JsonObject> definitions = new HashMap<>();
    private String safe(String s) { return s.replaceAll("[^a-zA-Z0-9_]", "_"); }
    private DataType type(String raw) {
        String name=raw.trim();
        if (types.containsKey(name)) return types.get(name);
        if (name.endsWith("&") || name.endsWith("*") || name.contains("[]"))
            return new PointerDataType(VoidDataType.dataType,8);
        JsonObject def=definitions.get(name);
        String base=def==null || def.get("base").isJsonNull()?"":def.get("base").getAsString();
        if (base.equals("System.Enum"))
            return IntegerDataType.dataType;
        if (def==null)
            return new PointerDataType(VoidDataType.dataType,8);
        boolean value=base.equals("System.ValueType");
        // Only recover concrete members when metadata actually describes
        // storage. Unity/runtime objects with no fields remain opaque.
        if (!value)
            return new PointerDataType(VoidDataType.dataType,8);
        StructureDataType result=new StructureDataType(safe(name),0);
        DataType recovered=value?result:new PointerDataType(result,8);
        types.put(name,recovered);
        for (JsonElement el:def.getAsJsonArray("fields")) {
            JsonObject f=el.getAsJsonObject();
            JsonObject offset=f.getAsJsonObject("offset");
            if (f.get("static").getAsBoolean() || !offset.has("Offset")) continue;
            // Cpp2IL's value-type offsets are already unboxed (start at zero).
            int off=Integer.decode(offset.get("Offset").getAsString());
            if (off<0) continue;
            DataType field=type(f.get("type").getAsString());
            if (result.getLength()<off+field.getLength())
                result.growStructure(off+field.getLength()-result.getLength());
            try {result.replaceAtOffset(off,field,field.getLength(),safe(f.get("name").getAsString()),null);}
            catch (Exception ignored) {}
        }
        return result.getLength()>0?recovered:new PointerDataType(VoidDataType.dataType,8);
    }
    private DataType selfType(String name) {
        String key="self:"+name;
        if(types.containsKey(key))return types.get(key);
        JsonObject def=definitions.get(name);
        if(def==null)return new PointerDataType(VoidDataType.dataType,8);
        StructureDataType result=new StructureDataType("Object_"+safe(name),0);
        for(JsonElement el:def.getAsJsonArray("fields")) {
            JsonObject f=el.getAsJsonObject();JsonObject offset=f.getAsJsonObject("offset");
            if(f.get("static").getAsBoolean() || !offset.has("Offset"))continue;
            int off=Integer.decode(offset.get("Offset").getAsString());
            DataType field=type(f.get("type").getAsString());
            if(result.getLength()<off+field.getLength())result.growStructure(off+field.getLength()-result.getLength());
            try {result.replaceAtOffset(off,field,field.getLength(),safe(f.get("name").getAsString()),null);}
            catch(Exception ignored) {}
        }
        DataType pointer=new PointerDataType(result,8);types.put(key,pointer);return pointer;
    }
    private void primitives() {
        types.put("System.Void",VoidDataType.dataType);
        types.put("System.Boolean",BooleanDataType.dataType);
        types.put("System.Byte",ByteDataType.dataType);
        types.put("System.SByte",SignedByteDataType.dataType);
        types.put("System.Int16",ShortDataType.dataType);
        types.put("System.UInt16",UnsignedShortDataType.dataType);
        types.put("System.Char",UnsignedShortDataType.dataType);
        types.put("System.Int32",IntegerDataType.dataType);
        types.put("System.UInt32",UnsignedIntegerDataType.dataType);
        types.put("System.Int64",LongLongDataType.dataType);
        types.put("System.UInt64",UnsignedLongLongDataType.dataType);
        types.put("System.Single",FloatDataType.dataType);
        types.put("System.Double",DoubleDataType.dataType);
        for (String n:new String[]{"Vector2","Vector3","Vector4","Quaternion"}) {
            StructureDataType t=new StructureDataType(n,0);
            for (int i=0;i<(n.equals("Vector2")?2:n.equals("Vector3")?3:4);i++)
                t.add(FloatDataType.dataType,new String[]{"x","y","z","w"}[i],null);
            types.put("UnityEngine."+n,t);
        }
    }
    @Override public void run() throws Exception {
        String[] args=getScriptArgs();
        // Ghidra rebases position-independent ELF imports to 0x100000 by
        // default. Catalog addresses are original ELF virtual addresses.
        if(currentProgram.getImageBase().getOffset()!=0)
            currentProgram.setImageBase(toAddr(0),true);
        JsonObject data=JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        Path out=Path.of(args[1]);Files.createDirectories(out);
        for(JsonElement t:data.getAsJsonArray("types")) {
            JsonObject def=t.getAsJsonObject();definitions.put(def.get("name").getAsString(),def);
        }
        primitives();
        List<Function> selected=new ArrayList<>();
        FunctionManager manager=currentProgram.getFunctionManager();
        // Discard earlier provisional function bodies before applying a new
        // catalog. A failed import must not leave overlapping stale symbols.
        List<Address> oldEntries=new ArrayList<>();
        for(Function old:manager.getFunctions(true))oldEntries.add(old.getEntryPoint());
        for(Address old:oldEntries)manager.removeFunction(old);
        int count=0;
        for(JsonElement el:data.getAsJsonArray("methods")) {
            if (monitor.isCancelled()) break;
            JsonObject m=el.getAsJsonObject();
            Address address=toAddr(m.get("address").getAsString());
            Function f=manager.getFunctionAt(address);
            if(f==null) {
                AddressSet body=new AddressSet(address,address.add(m.get("length").getAsInt()-1));
                try {f=manager.createFunction(safe(m.get("name").getAsString()),address,body,SourceType.IMPORTED);}
                catch(Exception ex) {continue;}
            }
            ArrayList<Variable> params=new ArrayList<>();
            if(m.get("instance").getAsBoolean())
                params.add(new ParameterImpl("self",selfType(m.get("type").getAsString()),currentProgram));
            int i=0;
            for(JsonElement arg:m.getAsJsonArray("params"))
                params.add(new ParameterImpl("arg"+i++,type(arg.getAsString()),currentProgram));
            params.add(new ParameterImpl("methodInfo",new PointerDataType(VoidDataType.dataType,8),currentProgram));
            try {
                f.updateFunction(null,new ReturnParameterImpl(type(m.get("returns").getAsString()),currentProgram),params,
                    Function.FunctionUpdateType.DYNAMIC_STORAGE_ALL_PARAMS,true,SourceType.IMPORTED);
            } catch(Exception ex) {println("Prototype: "+m.get("signature")+": "+ex.getMessage());}
            f.setComment(m.get("signature").getAsString()+"\nMetadata prototype; verify ABI against native instructions.");
            if(m.get("decompile").getAsBoolean()) selected.add(f);
            if(++count%10000==0)println("Imported "+count+" functions");
        }
        DecompInterface decomp=new DecompInterface();
        // These IL2CPP helpers always throw. Without this property the
        // decompiler invents fall-through into the next unrelated method.
        for(long entry:new long[]{0x316bb48L,0x316bb50L,0x316bb58L}) {
            Address a=toAddr(entry);
            Function fatal=manager.getFunctionAt(a);
            if(fatal==null)fatal=manager.createFunction("IL2CPP_Throw_"+Long.toHexString(entry),a,new AddressSet(a,a.add(7)),SourceType.IMPORTED);
            fatal.setReturnType(VoidDataType.dataType,SourceType.IMPORTED);
            fatal.setNoReturn(true);
        }
        for(Function f:manager.getFunctions(true))
            if(f.getName().contains("ThrowHelper__Throw"))f.setNoReturn(true);
        decomp.toggleCCode(true);decomp.toggleSyntaxTree(false);decomp.openProgram(currentProgram);
        int done=0;
        for(Function f:selected) {
            if(monitor.isCancelled())break;
            new DisassembleCommand(f.getEntryPoint(),f.getBody(),true).applyTo(currentProgram,monitor);
            DecompileResults result=decomp.decompileFunction(f,45,monitor);
            String code=result.decompileCompleted()?result.getDecompiledFunction().getC():"ERROR: "+result.getErrorMessage();
            Files.writeString(out.resolve(f.getName()+".c"),"// "+f.getComment()+"\n// VA "+f.getEntryPoint()+"\n"+code,StandardCharsets.UTF_8);
            if(++done%25==0)println("Decompiled "+done+" / "+selected.size());
        }
        decomp.dispose();println("Exported "+done+" functions to "+out);
    }
}
