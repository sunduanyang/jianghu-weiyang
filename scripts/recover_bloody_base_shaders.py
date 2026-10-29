"""Translate embedded no-keyword GLES shader operations to desktop HLSL.

Retains the original scalar/vector expressions rather than designing a visual
replacement. URP lighting variants and renderer globals require separate
runtime validation and are not certified by this translation step.
"""
import json
import re
import argparse
from pathlib import Path


def replace_calls(text, name, function):
    needle=name+'('
    while needle in text:
        start=text.index(needle);position=start+len(needle);depth=1;args=[];begin=position
        while depth:
            c=text[position]
            if c=='(':depth+=1
            elif c==')':depth-=1
            if c==',' and depth==1:args.append(text[begin:position]);begin=position+1
            position+=1
        args.append(text[begin:position-1])
        text=text[:start]+function([x.strip() for x in args])+text[position:]
    return text


def hlsl(text):
    text=replace_calls(text,'texture',lambda a:f'tex2Dbias({a[0]},float4({a[1]},0,{a[2] if len(a)>2 else "0"}))')
    text=replace_calls(text,'textureLod',lambda a:f'tex2Dlod({a[0]},float4({a[1]},0,{a[2]}))')
    text=re.sub(r'\b[bhml]?(?:lowp|mediump|highp)\s+','',text)
    for prefix,replacement in [('bvec','bool'),('ivec','int'),('uvec','uint'),('vec','float')]:
        text=re.sub(r'\b'+prefix+r'([234])\b',replacement+r'\1',text)
    for old,new in [('inversesqrt','rsqrt'),('fract','frac'),('dFdx','ddx'),('dFdy','ddy'),('mix','lerp')]:
        text=re.sub(r'\b'+old+r'\b',new,text)
    # GLSL vector equality returns a scalar; this uniform-splat comparison is
    # exactly equivalent to the source scalar uniform being nonzero.
    text=re.sub(r'float4\(0\.0, 0\.0, 0\.0, 0\.0\)!=float4\((_Erases_Bottom_of_L1)\)',r'(\1 != 0)',text)
    for old,new in [('hlslcc_mtx4x4unity_ObjectToWorld','unity_ObjectToWorld'),
                    ('hlslcc_mtx4x4unity_WorldToObject','unity_WorldToObject'),
                    ('hlslcc_mtx4x4unity_MatrixVP','UNITY_MATRIX_VP')]:
        text=re.sub(r'\b'+old+r'\[(\d+)\]',r'transpose('+new+r')[\1]',text)
    text=re.sub(r'\bhlslcc_mtx4x4(_\w+)\[(\d+)\]',r'transpose(\1)[\2]',text)
    text=re.sub(r'\bhlslcc_mtx4x4(unity_\w+)\[(\d+)\]',r'transpose(\1)[\2]',text)
    # GLES permits a scalar constructor to splat into a vector. Desktop HLSL
    # requires the explicit cast form; retain all original vector arities.
    for size in [2,3,4]:
        text=replace_calls(text,f'float{size}',lambda a,size=size:
            f'((float{size})({a[0]}))' if len(a)==1 else f'__ctor_f{size}('+','.join(a)+')')
    for size in [2,3,4]:text=text.replace(f'__ctor_f{size}(',f'float{size}(')
    return text


def program_parts(text):
    header,body=text.split('void main()',1)
    body=body[body.index('{')+1:body.rfind('}')]
    local='\n'.join(re.findall(r'^\s*(?:(?:mediump|highp|lowp)\s+)?(?:vec[234]|bvec[234]|uvec[234]|float|bool|int|uint|ivec[234])\s+(?:u_xlat\w+|TempArray\w+)(?:\[\d+\])?;',header,re.M))
    return header,local,body


def translate_program(raw, urp):
    vertex=raw.split('#ifdef VERTEX',1)[1].split('#ifdef FRAGMENT',1)[0]
    fragment=raw.split('#ifdef FRAGMENT',1)[1]
    vh,vl,vb=program_parts(vertex);fh,fl,fb=program_parts(fragment)
    interps={n:t for t,n in re.findall(r'(vec[234])\s+(vs_INTERP\d+)',vh)}
    for t,n in re.findall(r'^\s*(?:out|varying)\s+(?:(?:mediump|highp|lowp)\s+)?(vec[234]|float)\s+(\w+);',vh,re.M):interps[n]=t
    fields='\n'.join(f'{hlsl(t)} {n}:TEXCOORD{i};' for i,(n,t) in enumerate(interps.items()))
    uniforms={}
    for n in re.findall(r'\bhlslcc_mtx4x4(_\w+)\[4\]',vh+'\n'+fh):
        if not (urp and n in ['_PrevViewProjMatrix','_NonJitteredViewProjMatrix']):uniforms[n]='float4x4 '+n+';'
    builtins=['_Time','_SinTime','_CosTime','_ProjectionParams','_ScreenParams','_WorldSpaceCameraPos']
    if urp:builtins+=['_GlobalMipBias','unity_SpriteColor','unity_SpriteProps','unity_MotionVectorsParams','_TimeParameters','_LastTimeParameters','_AlphaToMaskAvailable','_PrevViewProjMatrix','_NonJitteredViewProjMatrix']
    for t,n,suffix in re.findall(r'(?:UNITY_UNIFORM|uniform)\s+(?:(?:mediump|highp|lowp)\s+)?(vec[234]|float|sampler2D)\s+(\w+)(\[\d+\])?;',vh+'\n'+fh):
        if n.startswith(('Xhlslcc_','hlslcc_mtx4x4')) or n in builtins:continue
        uniforms[n]=f'{hlsl(t)} {n}{suffix};'
    attributes=[('in_POSITION0','input.vertex'),('in_NORMAL0','input.normal'),('in_TEXCOORD0','input.texcoord'),('in_COLOR0','input.color'),('in_TANGENT0','input.tangent')]+[(f'in_TEXCOORD{i}',f'input.uv{i}') for i in range(1,8)]
    for src,dst in attributes:vb=re.sub(r'\b'+src+r'\b',dst,vb)
    for n in interps:
        vb=re.sub(r'\b'+n+r'\b','output.'+n,vb);fb=re.sub(r'\b'+n+r'\b','input.'+n,fb)
    vb=vb.replace('gl_Position','output.vertex').replace('return;','return output;')
    fullscreen='gl_VertexID' in vb
    if fullscreen:
        vb=vb.replace('gl_VertexID','input.vertexId').replace('return output;','output.vertex.z=(output.vertex.z+output.vertex.w)*0.5;\nreturn output;')
        # Retain source GLES coordinates through all screen-space arithmetic;
        # reflect render-target coordinates only when sampling on D3D. This
        # also preserves the rim field's source +y offset toward screen top.
        fb=replace_calls(fb,'texture',lambda a:f'OriginalSampleRT({a[0]},{a[1]},{a[2] if len(a)>2 else "0"})')
    fb=re.sub(r'\bSV_TARGET0\b','result',fb.replace('return;','return result;'),flags=re.I)
    fb=fb.replace('gl_FragCoord','sourceFragCoord')
    helpers='\n'.join(f'bool{i} greaterThanEqual(float{i} a,float{i} b){{return a>=b;}}\nbool{i} lessThan(float{i} a,float{i} b){{return a<b;}}' for i in [2,3,4])
    if fullscreen:helpers+='\nint int_bitfieldInsert(int baseValue,int insertValue,int offset,int bits) { uint mask=uint(~(int(~0)<<uint(bits))<<uint(offset));return int((uint(baseValue)&~mask)|((uint(insertValue)<<uint(offset))&mask)); }'
    if fullscreen:helpers+='\nfloat4 OriginalSampleRT(sampler2D sourceTexture,float2 sourceUV,float bias) {\n#if UNITY_UV_STARTS_AT_TOP\nsourceUV.y=1-sourceUV.y;\n#endif\nreturn tex2Dbias(sourceTexture,float4(sourceUV,0,bias));\n}'
    setup='SetUpSpriteInstanceProperties();' if urp else ''
    output=f"""{chr(10).join(uniforms.values())}
{helpers}
struct appdata {{ float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; }};
struct varying {{ float4 vertex:SV_POSITION;{fields} }};
varying vert(appdata input) {{ varying output=(varying)0;{setup}
{hlsl(vl)}
{hlsl(vb)}
}}
float4 frag(varying input):SV_Target {{ float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
{hlsl(fl)}
{hlsl(fb)}
}}
"""
    return output


def state_code(state):
    def val(k):return int(state[k]['val'])
    blends=['Zero','One','DstColor','SrcColor','OneMinusDstColor','SrcAlpha','OneMinusSrcColor','DstAlpha','OneMinusDstAlpha','SrcAlphaSaturate','OneMinusSrcAlpha']
    compares=['Disabled','Never','Less','Equal','LEqual','Greater','NotEqual','GEqual','Always']
    ops=['Keep','Zero','Replace','IncrSat','DecrSat','Invert','IncrWrap','DecrWrap']
    b=state['rtBlend0'];blend=lambda k:blends[int(b[k]['val'])]
    result=f"Cull {['Off','Front','Back'][val('culling')]} ZWrite {['Off','On'][val('zWrite')]} ZTest {compares[val('zTest')]}\n"
    result+=f"Blend {blend('srcBlend')} {blend('destBlend')}, {blend('srcBlendAlpha')} {blend('destBlendAlpha')}\n"
    front=state['stencilOpFront'];back=state['stencilOpBack']
    if val('stencilWriteMask')!=255 or val('stencilRef')!=0 or any(int(x['pass']['val'])!=0 for x in [front,back]):
        result+=f"Stencil {{ Ref {val('stencilRef')} ReadMask {val('stencilReadMask')} WriteMask {val('stencilWriteMask')} "
        for label,side in [('Front',front),('Back',back)]:
            result+=f"Comp{label} {compares[int(side['comp']['val'])]} Pass{label} {ops[int(side['pass']['val'])]} Fail{label} {ops[int(side['fail']['val'])]} ZFail{label} {ops[int(side['zFail']['val'])]} "
        result+='}\n'
    return result


def tags_code(tags):
    return 'Tags {'+' '.join('"'+('LightMode' if k.upper()=='LIGHTMODE' else k)+'"="'+v+'"' for k,v in tags['tags'])+'}'


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--shader-prefix',default='');args=parser.parse_args()
    repo=Path(__file__).resolve().parents[1]
    source=repo/'output/bloody-full-port-20261028/shader-programs'
    project=repo/'unity/BloodyBastardsRecovery/Assets/Shader'
    outputs=repo/'recovery/bloody/Shaders';outputs.mkdir(parents=True,exist_ok=True)
    ledger=[]
    lightkeys=[f'USE_SHAPE_LIGHT_TYPE_{i}' for i in range(4)]
    for directory in source.iterdir():
        if not directory.is_dir() or directory.name.startswith('Hidden_PaintCore_'):continue
        structure=json.loads((directory/'structure.json').read_text());parsed=structure['m_ParsedForm'];name=parsed['m_Name']
        if not name.startswith(args.shader_prefix):continue
        target=project/(name.replace('/','_')+'.shader')
        raw_shader=repo/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets/Shader'/target.name
        if not raw_shader.exists():continue
        stub=raw_shader.read_text();properties=stub[stub.index('Properties {'):stub.index('//DummyShaderTextExporter')]
        variants=json.loads((directory/'variants.json').read_text());by_index={v['index']:v for v in variants if v.get('executable')}
        urp=any(k=='RenderPipeline' and v=='UniversalPipeline' for k,v in parsed['m_SubShaders'][0]['m_Tags']['tags'])
        passes=[];translated=0
        for spass in parsed['m_SubShaders'][0]['m_Passes']:
            entries=[e for group in spass['progVertex'].get('m_PlayerSubPrograms',[]) for e in group]
            entries=[e for e in entries if e['m_BlobIndex'] in by_index and all(parsed['m_KeywordNames'][i] in lightkeys for i in e['m_KeywordIndices'])]
            if not entries:continue
            lighting=any(e['m_KeywordIndices'] for e in entries)
            programs=[]
            for i,e in enumerate(entries):
                variant=by_index[e['m_BlobIndex']];raw=(directory/variant['file']).read_text()
                if any(x in raw for x in ['sampler2DArray','sampler3D']):continue
                keys=[parsed['m_KeywordNames'][n] for n in e['m_KeywordIndices']]
                condition=' && '.join(('defined('+key+')') if key in keys else ('!defined('+key+')') for key in lightkeys)
                programs.append(('#if '+condition+'\n' if lighting else '')+translate_program(raw,urp)+('\n#endif' if lighting else ''))
                translated+=1
            state=spass['m_State']
            pragma='\n'.join('#pragma multi_compile _ '+k for k in lightkeys) if lighting else ''
            include='Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl' if urp else 'UnityCG.cginc'
            passes.append(f"""Pass {{
Name "{state['m_Name']}"
{tags_code(state['m_Tags'])}
{state_code(state)}
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5
{pragma}
#include "{include}"
{chr(10).join(programs)}
ENDHLSL
}}""")
        if not passes:continue
        if '_RendererColor' in '\n'.join(passes) and '_RendererColor (' not in properties:
            pos=properties.rfind('}');properties=properties[:pos]+'[PerRendererData] _RendererColor ("Renderer color", Color)=(1,1,1,1)\n'+properties[pos:]
        output=f'// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.\nShader "{name}" {{\n{properties}\nSubShader {{\n{tags_code(parsed["m_SubShaders"][0]["m_Tags"])}\n'+chr(10).join(passes)+'\n}\n}\n'
        output='\n'.join(line.rstrip() for line in output.splitlines())+'\n'
        saved=outputs/target.name;saved.write_text(output,encoding='utf-8')
        ledger.append(dict(shader=name,passes=len(passes),programs=translated,source=directory.relative_to(repo).as_posix(),
                           lighting_variants_ported=urp,unsupported_variants=['GPU SpriteSkin','stereo','DOTS','debug display'],render_parity_verified=False))
    status=outputs/'translation-status.json'
    if args.shader_prefix and status.exists():
        previous=json.loads(status.read_text());ledger=[r for r in previous if not r['shader'].startswith(args.shader_prefix)]+ledger
    status.write_text(json.dumps(ledger,indent=2),encoding='utf-8');print(json.dumps(ledger))
    if args.shader_prefix and not 'Hidden/PaintCore/CwDecalPointMultiply'.startswith(args.shader_prefix):return
    decal=source/'Hidden_PaintCore_CwDecal'
    # VFXAndDecalManager submits point decals with source blend index 10.
    # Retain that exact GPU program, including original alpha-preserving blend.
    raw=(decal/'9-0-51.glsl').read_text()
    program=translate_program(raw,False)
    # A UV-space mesh draw targets RT texels directly. Convert clip Y for
    # D3D-style texture coordinates, without changing source UV/world math.
    raster='output.vertex.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);'
    if program.count(raster)!=1:raise ValueError('Original paint UV raster assignment missing or ambiguous')
    program=program.replace(raster,raster+'\n#if UNITY_UV_STARTS_AT_TOP && !defined(ORIGINAL_PAINT_LEGACY_UV)\noutput.vertex.y = -output.vertex.y;\n#endif')
    paint='Shader "Hidden/PaintCore/CwDecalPointMultiply" {SubShader {Cull Off ZWrite Off ZTest Always Pass {HLSLPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n#pragma target 3.5\n#pragma multi_compile_local _ ORIGINAL_PAINT_LEGACY_UV\n#include "UnityCG.cginc"\n'+program+'\nENDHLSL\n}}}\n'
    (outputs/'Hidden_PaintCore_CwDecalPointMultiply.shader').write_text(paint,encoding='utf-8')


if __name__=='__main__':main()
