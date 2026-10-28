// Native C is an analysis aid; it is never compiled into the recovery game.
// Opaque class pointers avoid relying on invalid reconstructed SDK layouts.
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
import java.util.*;

public class ExportCombatPlain extends GhidraScript {
    Map<String,DataType> known=new HashMap<>();
    DataType type(String n) {
        if(known.containsKey(n))return known.get(n);
        return PointerDataType.dataType;
    }
    void primitives() {
        known.put("System.Void",VoidDataType.dataType);
        known.put("System.Boolean",BooleanDataType.dataType);
        known.put("System.Single",FloatDataType.dataType);
        known.put("System.Double",DoubleDataType.dataType);
        known.put("System.Int32",IntegerDataType.dataType);
        known.put("System.UInt32",UnsignedIntegerDataType.dataType);
        known.put("System.Int64",LongLongDataType.dataType);
        known.put("System.UInt64",UnsignedLongLongDataType.dataType);
        known.put("System.Byte",ByteDataType.dataType);
        known.put("System.SByte",SignedByteDataType.dataType);
        known.put("System.Int16",ShortDataType.dataType);
        known.put("System.UInt16",UnsignedShortDataType.dataType);
        known.put("System.Char",UnsignedShortDataType.dataType);
        known.put("Fusion.NetworkBool",IntegerDataType.dataType);
        known.put("Fusion.TickTimer",IntegerDataType.dataType);
        for(String n:new String[]{"Vector2","Vector3","Vector4","Quaternion"}) {
            StructureDataType s=new StructureDataType("Plain_"+n,0);
            for(int i=0;i<(n.equals("Vector2")?2:n.equals("Vector3")?3:4);i++)
                s.add(FloatDataType.dataType,new String[]{"x","y","z","w"}[i],null);
            known.put("UnityEngine."+n,s);
        }
    }
    public void run() throws Exception {
        String[] args=getScriptArgs();primitives();
        JsonObject catalog=JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        Path output=Path.of(args[1]);Files.createDirectories(output);
        FunctionManager manager=currentProgram.getFunctionManager();
        List<Function> selected=new ArrayList<>();
        Map<Function,String> signatures=new HashMap<>();
        for(JsonElement element:catalog.getAsJsonArray("methods")) {
            JsonObject m=element.getAsJsonObject();
            Address address=toAddr(m.get("address").getAsString());
            Function f=manager.getFunctionAt(address);
            if(f==null) {
                String symbol=m.get("name").getAsString().replaceAll("[^a-zA-Z0-9_]","_");
                try {f=manager.createFunction(symbol,address,new AddressSet(address,address.add(m.get("length").getAsInt()-1)),SourceType.IMPORTED);}
                catch(Exception e){println("Function rejected: "+symbol);continue;}
            }
            List<Variable> parameters=new ArrayList<>();
            if(m.get("instance").getAsBoolean())parameters.add(new ParameterImpl("self",PointerDataType.dataType,currentProgram));
            int index=0;
            for(JsonElement p:m.getAsJsonArray("params"))parameters.add(new ParameterImpl("arg"+index++,type(p.getAsString()),currentProgram));
            parameters.add(new ParameterImpl("methodInfo",PointerDataType.dataType,currentProgram));
            try {f.updateFunction(null,new ReturnParameterImpl(type(m.get("returns").getAsString()),currentProgram),parameters,Function.FunctionUpdateType.DYNAMIC_STORAGE_ALL_PARAMS,true,SourceType.IMPORTED);}
            catch(Exception e){println("Prototype rejected: "+m.get("signature"));}
            if(m.get("decompile").getAsBoolean()){selected.add(f);signatures.put(f,m.get("signature").getAsString());}
        }
        DecompInterface decomp=new DecompInterface();
        decomp.toggleCCode(true);decomp.toggleSyntaxTree(false);decomp.openProgram(currentProgram);
        int done=0,failed=0;
        for(Function f:selected) {
            if(monitor.isCancelled())break;
            new DisassembleCommand(f.getEntryPoint(),f.getBody(),true).applyTo(currentProgram,monitor);
            DecompileResults result=decomp.decompileFunction(f,30,monitor);
            String code=result.decompileCompleted()?result.getDecompiledFunction().getC():"ERROR: "+result.getErrorMessage();
            if(!result.decompileCompleted())failed++;
            Files.writeString(output.resolve(f.getName()+".c"),"// "+signatures.get(f)+"\n// VA "+f.getEntryPoint()+"\n// Opaque-class ABI approximation; verify against original ARM64 instructions.\n"+code);
            if(++done%50==0)println("Native listings "+done+" / "+selected.size()+"; errors "+failed);
        }
        decomp.dispose();
        Files.writeString(output.resolve("export-status.json"),"{\"exported\":"+done+",\"errors\":"+failed+",\"semantically_reviewed\":false}");
        println("Exported "+done+" native C listings; errors "+failed);
    }
}
