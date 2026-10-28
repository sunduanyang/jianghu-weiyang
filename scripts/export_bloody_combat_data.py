"""Export the supplied APK's combat dependency data without executing it.

Keeps original numeric curves, physics shapes, joints, transforms and source
identities. This is an extraction, not a claim that the runtime port is done.
"""
import argparse
import hashlib
import json
from pathlib import Path

import UnityPy
from UnityPy.classes.PPtr import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value, TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader


PLAYER = '00af04d95140a4b44ac8453d91c032ea'
ENEMY = 'c0b154b0ebaf72f4fa139b4e3cf363e9'
PLAYER_TWO = 'cdb759d5a1a317c4bbe2b091130bde38'
PHYSICS = {'Rigidbody2D','HingeJoint2D','FixedJoint2D','SpringJoint2D',
           'DistanceJoint2D','RelativeJoint2D','FrictionJoint2D','AreaEffector2D',
           'BoxCollider2D','CapsuleCollider2D','CircleCollider2D','PolygonCollider2D',
           'EdgeCollider2D','CompositeCollider2D'}


def export(args):
    env=UnityPy.load(str(args.serialized))
    files={f.name:f for f in env.files.values()}
    generator=TypeTreeGenerator('6000.4.0f1')
    generator.load_local_dll_folder(str(args.dlls))
    node_cache={}

    def typed(obj):
        header=obj.read_typetree(check_read=False)
        if obj.type.name!='MonoBehaviour':return header
        script=obj.read(check_read=False).m_Script.read()
        try:
            key=(script.m_AssemblyName,script.m_ClassName)
            if key not in node_cache:
                nodes=generator.get_nodes_up(*key)
                for child in nodes.m_Children:
                    if child.m_Name=='m_Enabled':child.m_MetaFlag|=0x4000
                for node in nodes.traverse():
                    # Generator labels List<string> as string-with-Array.
                    # UnityPy's special string reader otherwise consumes it as
                    # one scalar string and corrupts all following fields.
                    if (node.m_Type=='string' and node.m_Children
                            and node.m_Children[0].m_Type=='Array'
                            and node.m_Children[0].m_Children[-1].m_Type=='string'):
                        node.m_Type='vector'
                node_cache[key]=nodes
            # The native reader caches the generator's original string dispatch.
            # Use the Python reader for repaired custom schemas and require an
            # exact byte count so a malformed field cannot silently shift data.
            reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian)
            data=read_value(node_cache[key],reader,TypeTreeConfig(True,obj.assets_file,False))
            if reader.Position!=obj.byte_size:
                raise ValueError(f'Consumed {reader.Position} of {obj.byte_size} bytes')
        except Exception as error:
            raise ValueError(f'Typed decode failed: {obj.assets_file.name}:{obj.path_id} {script.m_ClassName}') from error
        for key in ('m_GameObject','m_Enabled','m_Script','m_Name'):
            if key in header:data[key]=header[key]
        return data

    def deref(file,ref):
        return PPtr(m_FileID=ref['m_FileID'],m_PathID=ref['m_PathID'],assetsfile=file).deref()

    def scripts(file,wanted):
        result={}
        for obj in file.objects.values():
            if obj.type.name!='MonoBehaviour':continue
            script=obj.read(check_read=False).m_Script.read()
            if script.m_ClassName in wanted:
                result.setdefault(script.m_ClassName,[]).append(dict(id=obj.path_id,data=typed(obj)))
        return result

    def identity(obj):return dict(file=obj.assets_file.name,id=obj.path_id)

    clips={}
    def clip(file,ref):
        obj=deref(file,ref);key=obj.assets_file.name+':'+str(obj.path_id)
        if key not in clips:
            data=obj.read_typetree()
            keys=('m_Name','m_Legacy','m_Compressed','m_SampleRate','m_WrapMode',
                  'm_PositionCurves','m_EulerCurves','m_RotationCurves','m_ScaleCurves',
                  'm_FloatCurves','m_Events','m_CompressedRotationCurves')
            record={k:data[k] for k in keys if k in data}
            times=[float(k['time']) for channel in ('m_PositionCurves','m_EulerCurves',
                   'm_RotationCurves','m_ScaleCurves','m_FloatCurves')
                   for curve in data.get(channel,[]) for k in curve['curve']['m_Curve']]
            record.update(source=identity(obj),duration=max(times,default=0))
            clips[key]=record
        return key

    rigs={}
    materials={}
    for name in (PLAYER,ENEMY,PLAYER_TWO):
        file=files[name];objects={};transforms={};physics=[]
        for obj in file.objects.values():
            if obj.type.name=='GameObject':objects[str(obj.path_id)]=obj.read_typetree()
            elif obj.type.name in ('Transform','RectTransform'):transforms[str(obj.path_id)]=obj.read_typetree()
            elif obj.type.name in PHYSICS:
                data=obj.read_typetree();physics.append(dict(id=obj.path_id,type=obj.type.name,data=data))
                for key in ('m_Material',):
                    ref=data.get(key,{})
                    if ref.get('m_PathID'):
                        mat=deref(file,ref);materials[mat.assets_file.name+':'+str(mat.path_id)]=mat.read_typetree()
        controllers=scripts(file,{'CharacterMover','CharacterVitality','CharacterDamageCalculator',
                                  'BodyPart','InternalOrgan','KickReceiver','CharacterEnemyAI',
                                  'AIHandsInputRelayer','CharacterConfigApplier','StaminaDrainer',
                                  'WeaponSelfColliderInitializer','CharacterAnimationController',
                                  'RigPoseResetter','KienematicDynamicEnabler'})
        mover=controllers['CharacterMover'][0]['data']
        animation=deref(file,mover['kickAnimation']).read()
        kick=clip(animation.m_Animation.assetsfile,dict(m_FileID=animation.m_Animation.m_FileID,m_PathID=animation.m_Animation.m_PathID))
        rigs[name]=dict(source=name,objects=objects,transforms=transforms,
                        physics=physics,controllers=controllers,kick_clip=kick,
                        externals=[e.path for e in file.externals])

    enemy=files[ENEMY]
    relayer=rigs[ENEMY]['controllers']['AIHandsInputRelayer'][0]['data']
    bundles={}
    for field,ref in relayer.items():
        if not field.startswith('AIHandsAnimations_'):continue
        obj=deref(enemy,ref);data=typed(obj)
        bundles[field.replace('AIHandsAnimations_','')]=dict(source=identity(obj),
            name=data['m_Name'],up=[clip(obj.assets_file,r) for r in data['AttackingUp']],
            down=[clip(obj.assets_file,r) for r in data['AttackingDown']])

    # Preserve item identities and the transform/physics graphs in their source
    # files, including chain links and counterweight bodies. Other managed
    # component dependencies still require a separate code/data audit.
    items=[];item_graphs={};scene_item_sources=[];null_script_sources=[]
    for file in files.values():
        for obj in file.objects.values():
            if obj.type.name!='MonoBehaviour':continue
            pointer=obj.read(check_read=False).m_Script
            if not pointer.m_PathID:
                null_script_sources.append(identity(obj));continue
            script=pointer.read()
            if script.m_ClassName not in ('SourcePrefab_HandItem','SourcePrefab_ArmorItem'):
                continue
            items.append(dict(source=identity(obj),script=script.m_ClassName,data=typed(obj)))
            if file.name in item_graphs:continue
            if file.name.startswith('level'):
                if file.name not in scene_item_sources:scene_item_sources.append(file.name)
                continue
            graph=dict(objects={},transforms={},physics=[])
            for component in file.objects.values():
                kind=component.type.name
                if kind=='GameObject':graph['objects'][str(component.path_id)]=component.read_typetree()
                elif kind in ('Transform','RectTransform'):graph['transforms'][str(component.path_id)]=component.read_typetree()
                elif kind in PHYSICS:graph['physics'].append(dict(id=component.path_id,type=kind,data=component.read_typetree()))
            item_graphs[file.name]=graph

    result=dict(schema=1,source_apk_sha256=hashlib.file_digest(args.apk.open('rb'),'sha256').hexdigest(),
        source_unity='6000.4.0f1',rigs=rigs,ai_bundles=bundles,clips=clips,
        materials=materials,items=items,item_graphs=item_graphs,scene_item_sources=scene_item_sources,
        null_script_sources=null_script_sources,
        scope='Numeric dependency extraction; runtime equivalence must be tested separately')
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps(dict(rigs=len(rigs),bundles=len(bundles),clips=len(clips),items=len(items),
                         physics={k:len(v['physics']) for k,v in rigs.items()})))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('apk','serialized','dlls','output'):parser.add_argument('--'+name,type=Path,required=True)
    export(parser.parse_args())
