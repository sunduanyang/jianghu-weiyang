"""Bind independent exploded components; final illustrations are comparison only.

Drawable pixels come exclusively from the board's right-hand component panels.
The final illustration supplies body proportions and visual comparison only.
Shoulders and limb attachments use the source panels; arms have a neutral bind.
"""
import json, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from bake_paper_cast import ROOT, SOURCE, DEST, RESOURCE_ROOT, pack, texture_meta, digest

SEGMENTS = {'torso': ('hip', 'neck', 'torso', 40)}
for side, prefix in [('Near', 'near'), ('Far', 'far')]:
    for name, a, b, bone, layer in [
        ('upper-arm','shoulder','elbow','upper',72 if side=='Near' else 10),
        ('forearm','elbow','wrist','forearm',75 if side=='Near' else 12),
        ('thigh','hip','knee','thigh',25 if side=='Near' else 16),
        ('shin','knee','ankle','shin',26 if side=='Near' else 17)]:
        SEGMENTS[prefix+'-'+name] = (a+side, b+side, bone+side, layer)

def rotate(point, degrees):
    a=math.radians(degrees);c,s=math.cos(a),math.sin(a)
    return np.array([c*point[0]-s*point[1],s*point[0]+c*point[1]])

def rotation(degrees):
    a=math.radians(degrees);c,s=math.cos(a),math.sin(a)
    return np.array([[c,-s],[s,c]])

def down(v): return math.degrees(math.atan2(-v[0], v[1]))

def load_calibrations():
    result={}
    for path in sorted(SOURCE.glob('split-calibration-??-??.json')):
        for cid, value in json.loads(path.read_text(encoding='utf-8'))['characters'].items():
            if cid in result: raise ValueError('Duplicate split calibration '+cid)
            result[cid]=value
    return result

_previous_path=ROOT/'unity/JianghuUnity/Assets/Resources/PaperCast/index.json'
_previous_index=json.loads(_previous_path.read_text(encoding='utf-8')) if _previous_path.exists() else {}

def source_rect(sheet, image, cid, name, extraction_mirror=False):
    """Prove and locate the unmodified component inside the exploded panel area."""
    previous=_previous_index.get('skins',{}).get(cid,{}).get('parts',{}).get(name,{})
    if previous.get('source_rect'):
        crop=sheet.crop(previous['source_rect']).convert('RGB')
        if previous.get('source_extraction_mirror'):crop=crop.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        a=np.asarray(image);b=np.asarray(crop);solid=a[:,:,3]>250
        if a.shape[:2]==b.shape[:2] and np.array_equal(a[:,:,:3][solid],b[solid]):return previous['source_rect'],previous['source_extraction_mirror']
    a=np.asarray(image);rgb=np.asarray(sheet.convert('RGB'))
    yy,xx=np.nonzero((a[:,:,3]>250)&(a[:,:,:3].min(2)<140))
    choice=np.linspace(0,len(xx)-1,min(32,len(xx)),dtype=int);xx,yy=xx[choice],yy[choice]
    value=a[yy[0],xx[0],:3]
    sy,sx=np.nonzero(np.all(rgb==value,axis=2));ox,oy=sx-xx[0],sy-yy[0]
    valid=(ox>=420)&(oy>=90)&(ox+image.width<=sheet.width)&(oy+image.height<=sheet.height)
    ox,oy=ox[valid],oy[valid]
    for x,y in zip(xx[1:],yy[1:]):
        valid=np.all(rgb[oy+y,ox+x]==a[y,x,:3],axis=1);ox,oy=ox[valid],oy[valid]
    for x,y in zip(ox,oy):
        solid=a[:,:,3]>250
        if np.array_equal(rgb[y:y+image.height,x:x+image.width][solid],a[:,:,:3][solid]):
            return [int(x),int(y),int(x+image.width),int(y+image.height)],extraction_mirror
    if not extraction_mirror:
        return source_rect(sheet,image.transpose(Image.Transpose.FLIP_LEFT_RIGHT),cid,name,True)
    raise ValueError(f'{cid}/{name}: component does not match the original right-side panel')

def masked_part(image, config):
    a=np.array(image);mask=Image.new('L', image.size, 255)
    if 'keep_polygon' in config:
        mask=Image.new('L',image.size);ImageDraw.Draw(mask).polygon([tuple(p) for p in config['keep_polygon']],fill=255)
    for points in config.get('erase_polygons',[]):
        ImageDraw.Draw(mask).polygon([tuple(p) for p in points],fill=0)
    if 'restore_lower_body' in config:
        # Retain the authored coat tails below the belt. The sleeve cleanup
        # above the waist must not turn into a horizontal amputation of cloth.
        recipe=config['restore_lower_body'];lower=Image.new('L',image.size,255)
        if 'keep_polygon' in recipe:
            lower=Image.new('L',image.size);ImageDraw.Draw(lower).polygon([tuple(p) for p in recipe['keep_polygon']],fill=255)
        for points in recipe.get('erase_polygons',[]):
            ImageDraw.Draw(lower).polygon([tuple(p) for p in points],fill=0)
        m=np.asarray(mask).copy();retained=np.asarray(lower);start=max(0,int(recipe['waist_y'])-2)
        m[start:]=np.maximum(m[start:],retained[start:]);mask=Image.fromarray(m)
    a[:,:,3]=np.minimum(a[:,:,3],np.asarray(mask))
    if config.get('keep_connected_body'):
        from scipy import ndimage
        labels,_=ndimage.label(a[:,:,3]>0);areas=np.bincount(labels.ravel());areas[0]=0
        a[labels!=areas.argmax()]=0
    pivot=np.array(config['pivot'],dtype=float);tip=np.array(config.get('tip',[0,0]),dtype=float)
    if config.get('clip_tip'):
        y,x=np.mgrid[:image.height,:image.width];v=tip-pivot
        t=((x-pivot[0])*v[0]+(y-pivot[1])*v[1])/float(v@v)
        a[t>1.025,3]=0
    if config.get('mirror'):
        a=a[:,::-1].copy();pivot[0]=image.width-pivot[0]
        if 'tip' in config:tip[0]=image.width-tip[0]
    a[a[:,:,3]==0]=0
    return Image.fromarray(a),pivot,tip

def knee(hip,target,upper,lower):
    delta=target-hip;d=min(max(np.linalg.norm(delta),abs(upper-lower)+.0001),upper+lower-.0001);axis=delta/np.linalg.norm(delta)
    along=(upper*upper-lower*lower+d*d)/(2*d);bend=math.sqrt(max(0,upper*upper-along*along))
    return hip+axis*along+np.array([axis[1],-axis[0]])*bend

def pose(skin,phase=0,walking=False):
    weight=float(walking);cycle=phase*2*math.pi;lean=skin['bind_lean']+weight*1.5
    p={'hip':np.array([0.,weight*skin['bob']*(1-math.cos(cycle*2))*.5])};angles={'body':lean,'head':lean-skin['bind_lean'],'pelvis':lean-skin['bind_lean']+weight*2*math.sin(cycle)}
    goals={}
    for side in ('Near','Far'):
        q=(phase+(0 if side=='Near' else .5))%1
        if q<.6:x,lift=1-2*q/.6,0
        else:t=(q-.6)/.4;x,lift=-math.cos(t*math.pi),math.sin(t*math.pi)**2
        goals[side]=np.array(skin['feet'][side])+np.array([x*skin['stride'],-lift*skin['lift']])*weight
        off=rotate(skin['hips'][side],lean);reach=skin['lengths']['thigh'+side]+skin['lengths']['shin'+side]-.001;dx=goals[side][0]-off[0]
        p['hip'][1]=max(p['hip'][1],goals[side][1]-off[1]-math.sqrt(max(.001,reach*reach-dx*dx)))
    p['neck']=p['hip']+rotate([0,-skin['lengths']['spine']],lean)
    for side in ('Far','Near'):
        opposite=1 if side=='Near' else -1
        p['shoulder'+side]=p['hip']+rotate(skin['shoulders'][side],lean)
        upper=skin['arm_rest'][side][0]+lean+weight*opposite*18*math.cos(cycle)
        fore=upper+skin['arm_rest'][side][1]-weight*(5+5*math.sin(cycle)*opposite)
        p['elbow'+side]=p['shoulder'+side]+rotate([0,skin['lengths']['upper_arm'+side]],upper)
        p['wrist'+side]=p['elbow'+side]+rotate([0,skin['lengths']['forearm'+side]],fore)
        p['hip'+side]=p['hip']+rotate(skin['hips'][side],lean)
        p['ankle'+side]=goals[side]
        p['knee'+side]=knee(p['hip'+side],goals[side],skin['lengths']['thigh'+side],skin['lengths']['shin'+side])
        angles['forearm'+side]=angles['hand'+side]=fore;angles['foot'+side]=0
    return p,angles

def render(skin,images,rig,phase=0,walking=False,bones=False):
    w,h=skin['reference_size'];unit=skin['pixel_scale'];origin=np.array(skin['reference_origin']);p,angles=pose(skin,phase,walking)
    points={k:v/unit+origin for k,v in p.items()};result=Image.new('RGBA',(w,h))
    for name,part in sorted(skin['parts'].items(),key=lambda kv:kv[1]['layer']):
        if part.get('hidden'):continue
        im=images[name];pivot=np.array(part['pivot']);breadth=part.get('breadth',1)
        if 'bone' in part:
            start,end=rig['bones'][part['bone']];a,b=points[start],points[end];local=np.array(part['tip'])-pivot;e=local/np.linalg.norm(local)
            if part.get('freeze_to_body'):
                delta=np.array(skin['bind_joints'][end])-np.array(skin['bind_joints'][start])
                b=a+rotate(delta,angles['body']-skin['bind_lean'])/unit
            scale=np.linalg.norm(b-a)/np.linalg.norm(local);angle=math.degrees(math.atan2(*(b-a)[::-1])-math.atan2(*local[::-1]))
        else:
            a=points[part['joint']];scale=part['height']/im.height/unit;angle=part['angle']+angles[part['follow']];e=np.array([0.,-1.])
            if name=='head-neck':scale=part['height']/max(pivot[1],1)/unit
        q=np.array([e[1],-e[0]]);linear=rotation(angle)@(np.outer(e,e)+breadth*np.outer(q,q))*scale
        offset=a-linear@pivot;inverse=np.linalg.inv(linear);transform=np.column_stack([inverse,-inverse@offset])
        layer=im.transform((w,h),Image.Transform.AFFINE,tuple(transform.flatten()),Image.Resampling.BICUBIC);result=Image.alpha_composite(result,layer)
    if bones:
        d=ImageDraw.Draw(result)
        for a,b in rig['bones'].values():d.line([tuple(points[a]),tuple(points[b])],fill=(35,180,190,255),width=2)
        for x,y in points.values():d.ellipse((x-3,y-3,x+3,y+3),fill=(220,65,40,255))
    return result

def make_skeleton(character,cal,rig,reference):
    cid=character['id'];w,h=reference.size;points={k:np.array(v,dtype=float) for k,v in cal['landmarks'].items()}
    if cal['mirror']:points={k:np.array([w-p[0],p[1]]) for k,p in points.items()}
    hip=points['hip'];unit=(375 if 'child' in cid else 555 if 'elder' in cid else 600)/h;j={k:(v-hip)*unit for k,v in points.items()}
    lean=math.degrees(math.atan2(j['neck'][0],-j['neck'][1]));lengths={'spine':float(np.linalg.norm(j['neck']))}
    skin=dict(id=cid,description=character['name']+'，'+character['desc'],atlas=cid,scale=1,binding_basis='independent exploded component panels',
        reference_image=cid,reference_source=character['file'],reference_source_rect=cal['source_rect'],reference_mirror=cal['mirror'],
        pixel_scale=unit,reference_origin=hip.tolist(),reference_size=[w,h],canvas=[620,700],
        canvas_origin=[310+(hip[0]-w/2)*unit,650-(h-hip[1])*unit],bind_lean=lean,lengths=lengths,
        shoulders={s:rotate(j['shoulder'+s],-lean).tolist() for s in ('Near','Far')},hips={s:rotate(j['hip'+s],-lean).tolist() for s in ('Near','Far')},
        feet={s:j['ankle'+s].tolist() for s in ('Near','Far')},arm_rest={'Near':[-3,-7],'Far':[5,-7]},sole={},parts={})
    for kind,start,end in [('upper_arm','shoulder','elbow'),('forearm','elbow','wrist'),('thigh','hip','knee'),('shin','knee','ankle')]:
        values={s:float(np.linalg.norm(j[end+s]-j[start+s])) for s in ('Near','Far')}
        # Foreshortened tool poses cannot define the length of an empty arm.
        if kind in ('upper_arm','forearm'):values={s:max(values.values()) for s in ('Near','Far')}
        for s,value in values.items():lengths[kind+s]=value
        lengths[kind]=sum(values.values())/2
    skin.update(stride=(lengths['thigh']+lengths['shin'])*.12,lift=(lengths['thigh']+lengths['shin'])*.09,bob=(lengths['thigh']+lengths['shin'])*.008)
    return skin

def build_skin(character,cal,config,rig,reference):
    cid=character['id'];skin=make_skeleton(character,cal,rig,reference);w,h=reference.size;unit=skin['pixel_scale']
    sheet=Image.open(SOURCE/character['file']);images={};directory=SOURCE/cid/'split-bound-parts';directory.mkdir(exist_ok=True)
    for name,original_cfg in config['parts'].items():
        cfg=dict(original_cfg)
        cfg.update(json.loads((SOURCE/'walk-socket-overrides.json').read_text(encoding='utf-8'))['characters'].get(cid,{}).get(name,{}))
        if name=='prop' and cfg.get('joint','hip').startswith('wrist'):continue
        component=cfg.get('source_component',name);path=SOURCE/cid/(component+'.png');original=Image.open(path).convert('RGBA');rect,extraction_mirror=source_rect(sheet,original,cid,name)
        # Only the printed sleeve is cut away. Keep the torso's own lower
        # cloth, which supplies the rear hip silhouette behind the legs.
        if name=='torso' and not cfg.get('preserve_original_body'):
            lower_recipe={k:cfg[k] for k in ('keep_polygon','erase_polygons') if k in cfg}
            waist=np.array(cfg['pivot']);neck=np.array(cfg['tip']);length=np.linalg.norm(neck-waist)
            # Remove the sleeves printed into the torso panel; independent
            # upper arms must own those pixels around the shoulder hinge.
            cfg['keep_polygon']=[[neck[0]-.22*length,0],[neck[0]+.23*length,0],
                [neck[0]+.39*length,neck[1]+.42*length],[waist[0]+.46*length,waist[1]+4],
                [waist[0]-.43*length,waist[1]+4],[neck[0]-.28*length,neck[1]+.32*length]]
            cfg.pop('erase_polygons',None)
            if cfg.get('restore_authored_hem'):
                cfg['restore_lower_body']=dict(lower_recipe,waist_y=float(waist[1]))
            cfg['keep_connected_body']=True
        if name.endswith('foot'):
            y=max(0,cfg['pivot'][1]-original.height*.09)
            cfg['keep_polygon']=[[0,y],[original.width,y],[original.width,original.height],[0,original.height]]
        cfg.pop('hidden',None)
        image,pivot,tip=masked_part(original,cfg);out=directory/(name+'.png');image.save(out);images[name]=image
        p=dict(pivot=pivot.tolist(),size=list(image.size),breadth=cfg.get('breadth',1),source=f'{cid}/split-bound-parts/{name}.png',sha256=digest(out),
            source_component=f'{cid}/{component}.png',source_component_sha256=digest(path),source_sheet=character['file'],source_rect=rect,
            source_region='right-hand exploded component panel',source_extraction_mirror=extraction_mirror,source_mirror=cfg.get('mirror',False),
            source_mask={k:cfg[k] for k in ('keep_polygon','erase_polygons','clip_tip','restore_lower_body','keep_connected_body') if k in cfg})
        if name in SEGMENTS:
            start,end,bone,layer=SEGMENTS[name];p.update(bone=bone,layer=cfg.get('layer',layer),tip=tip.tolist())
        else:
            side='Near' if name.startswith('near') else 'Far'
            joint='neck' if name=='head-neck' else 'hip' if name=='pelvis' else ('wrist' if name.endswith('hand') else 'ankle')+side if name.endswith(('hand','foot')) else cfg.get('joint','hip')
            follow='head' if name=='head-neck' else 'pelvis' if name=='pelvis' else 'hand'+side if name.endswith('hand') else 'foot'+side if name.endswith('foot') else 'body'
            rest=cfg.get('rest_angle',0);height=cfg['target_height_fraction']*h*unit
            if name.endswith('hand'):
                a=np.array(image);ys,xs=np.nonzero(a[:,:,3]>127);vectors=np.column_stack([xs,ys])-pivot
                radius=np.linalg.norm(vectors,axis=1);direction=vectors[radius>=np.quantile(radius,.85)].mean(axis=0)
                rest=-down(direction)
            if name=='pelvis':
                # Each costume has its own measured hem length; no blanket
                # 28-percent shortening. Keep the calibrated waistband width.
                p['breadth']=original_cfg.get('breadth',1)*original_cfg['target_height_fraction']/cfg['target_height_fraction']
            if name=='head-neck':height*=pivot[1]/image.height
            layer=60 if name=='head-neck' else 50 if name=='pelvis' else 80 if name=='near-hand' else 14 if name=='far-hand' else 29 if name=='near-foot' else 20 if name=='far-foot' else 82
            p.update(joint=joint,follow=follow,angle=rest-(skin['bind_lean'] if follow=='body' else 0),rest_angle=rest,height=height,layer=cfg.get('layer',layer))
        if cfg.get('hidden'):p['hidden']=True
        skin['parts'][name]=p
    # Shoulder sockets are measured in the torso's source frame, then
    # transformed into the common skeleton frame. A pose with a prop no
    # longer decides where an independent sleeve connects.
    torso=skin['parts']['torso'];local=np.array(torso['tip'])-np.array(torso['pivot']);length=np.linalg.norm(local)
    world=np.array([0.,-skin['lengths']['spine']]);angle=math.degrees(math.atan2(world[1],world[0])-math.atan2(local[1],local[0]));e=local/length;q=np.array([e[1],-e[0]])
    linear=rotation(angle)@(np.outer(e,e)+torso['breadth']*np.outer(q,q))*skin['lengths']['spine']/length
    for side,offset in [('Near',[-.25,.27]),('Far',[.20,.27])]:
        source=np.array(torso['tip'])+np.array(offset)*length
        skin['shoulders'][side]=(linear@(source-np.array(torso['pivot']))).tolist()
    skin['ground']=(h-4-skin['reference_origin'][1])*unit
    for side,prefix in [('Near','near'),('Far','far')]:
        foot=skin['parts'][prefix+'-foot'];bbox=images[prefix+'-foot'].getbbox();sole=(bbox[3]-1-foot['pivot'][1])*foot['height']/foot['size'][1]
        skin['sole'][side]=sole
    # Both shoe soles share one floor, even when the comparison drawing put
    # the far foot higher. Root compensation preserves both leg lengths.
    seat_parts=[]
    for side in ('Near','Far'):
        hip=rotate(skin['hips'][side],skin['bind_lean']);skin['feet'][side]=[float(hip[0]+(3 if side=='Near' else -3)*unit),skin['ground']-skin['sole'][side]]
        required=(skin['feet'][side][1]-hip[1])/.985;old=skin['lengths']['thigh'+side]+skin['lengths']['shin'+side]
        for kind in ('thigh','shin'):skin['lengths'][kind+side]*=required/old
    for kind in ('thigh','shin'):skin['lengths'][kind]=(skin['lengths'][kind+'Near']+skin['lengths'][kind+'Far'])/2
    skin['canvas_origin'][1]=650-skin['ground']
    points,angles=pose(skin)
    # Measure the body's real waistband span; the two trouser panels must not
    # create two broad, square ledges sticking out above that waistband.
    ta=np.array(images['torso']);ty,tx=np.nonzero(ta[:,:,3]>127)
    te=local/np.linalg.norm(local);tq=np.array([te[1],-te[0]])
    tv=np.column_stack([tx,ty])-np.array(torso['pivot']);ts=skin['lengths']['spine']/np.linalg.norm(local)
    body_y=-(tv@te)*ts;body_x=-(tv@tq)*ts*torso['breadth'];belt=np.abs(body_y)<4
    waist_span=[float(np.quantile(body_x[belt],.01)),float(np.quantile(body_x[belt],.99))]
    for side in ('Near','Far'):
        # A thigh panel starts at the trouser waistband, not at the hip joint.
        # Place the hinge INSIDE that panel so its upper pixels cover the seat.
        # The skeleton, knee, shoe and perpendicular artwork width stay fixed.
        if config['parts']['pelvis']['target_height_fraction']>.36:continue
        name=side.lower()+'-thigh';part=skin['parts'][name]
        anchor=np.array(part['pivot']);tip=np.array(part['tip']);v=tip-anchor;source_length=np.linalg.norm(v)
        axis=(points['knee'+side]-points['hip'+side])/skin['lengths']['thigh'+side]
        a=np.array(images[name]);yy,xx=np.nonzero(a[:,:,3]>127)
        local=(np.column_stack([xx,yy])-anchor);e=v/source_length;q=np.array([e[1],-e[0]])
        projection=local@e;cross=local@q;top=float(projection.min());length=skin['lengths']['thigh'+side]
        # Solve the actual upper silhouette, including diagonal waistband
        # corners, rather than allowing a corner to poke above the belt.
        def upper_y(shift):
            return float((points['hip'+side][1]+axis[1]*(projection-shift)*length/(source_length-shift)-axis[0]*cross*part['breadth']*length/source_length).min())
        lo,hi=0.,source_length*.6;waist_y=points['hip'][1]+4
        if upper_y(lo)>waist_y:
            for _ in range(35):
                mid=(lo+hi)/2
                if upper_y(mid)>waist_y:lo=mid
                else:hi=mid
        shift=lo
        if shift>=source_length*.65:raise ValueError(cid+'/'+name+': hip hinge lies beyond the upper thigh')
        part['waist_source_anchor']=anchor.tolist();part['waist_top_projection']=top
        part['pivot']=(anchor+v/source_length*shift).tolist();part['breadth']*=(source_length-shift)/source_length
        part['binding_role']='hip hinge inside full waist-to-knee trouser panel'
        # The newly recovered upper trouser pixels belong to the pelvis.
        # Keep them on the body while only the lower section follows the leg.
        # Otherwise the rear seat would swing open whenever the thigh rotates.
        yy,xx=np.mgrid[:a.shape[0],:a.shape[1]];along=(xx-anchor[0])*e[0]+(yy-anchor[1])*e[1]
        overlap=max(6.,source_length*.12);seat=np.array(a);seat[along>shift+overlap,3]=0
        leg=np.array(a);leg[along<shift-overlap,3]=0
        # Shape only the hidden upper waistband corners to the body's outline.
        # Retain those covered pixels as a separate editable source layer.
        uv=np.column_stack([(xx-part['pivot'][0]).ravel(),(yy-part['pivot'][1]).ravel()])
        source_e=(tip-np.array(part['pivot']));source_e/=np.linalg.norm(source_e);source_q=np.array([source_e[1],-source_e[0]])
        screen_angle=math.degrees(math.atan2(axis[1],axis[0])-math.atan2(source_e[1],source_e[0]))
        linear=rotation(screen_angle)@(np.outer(source_e,source_e)+part['breadth']*np.outer(source_q,source_q))*length/(source_length-shift)
        body=(uv@linear.T+points['hip'+side]-points['hip'])@rotation(-skin['bind_lean']).T
        sx=body[:,0].reshape(xx.shape);sy=body[:,1].reshape(xx.shape);solid=seat[:,:,3]>127
        bounds=[float(sx[solid].min()),float(sx[solid].max())];hip_depth=float(rotate(skin['hips'][side],0)[1])
        ease=np.sin(np.clip(sy/max(12,hip_depth*.85),0,1)*math.pi/2)
        back=waist_span[0]+(bounds[0]-waist_span[0])*ease;front=waist_span[1]+(bounds[1]-waist_span[1])*ease
        concealed=(sx<back)|(sx>front);hidden=np.array(seat);hidden[~concealed,3]=0;seat[concealed,3]=0
        for pixels in (seat,leg,hidden):pixels[pixels[:,:,3]==0]=0
        images[name]=Image.fromarray(leg);leg_path=directory/(name+'.png');images[name].save(leg_path);part['sha256']=digest(leg_path)
        part['source_mask']['axial_clip_after_mirror']=dict(origin=anchor.tolist(),axis=e.tolist(),minimum=shift-overlap)
        seat_name=side.lower()+'-seat';images[seat_name]=Image.fromarray(seat);seat_path=directory/(seat_name+'.png');images[seat_name].save(seat_path)
        hidden_path=directory/(seat_name+'-waist-hidden.png');Image.fromarray(hidden).save(hidden_path)
        seat_part={k:v for k,v in part.items() if k not in ('source_mask','sha256','source','binding_role')}
        seat_part.update(source=f'{cid}/split-bound-parts/{seat_name}.png',sha256=digest(seat_path),freeze_to_body=True,layer=34 if side=='Near' else 32,
            source_mask=dict(axial_clip_after_mirror=dict(origin=anchor.tolist(),axis=e.tolist(),maximum=shift+overlap),waist_envelope=dict(span=waist_span,hip_depth=hip_depth)),
            occluded_source=f'{cid}/split-bound-parts/{hidden_path.name}',occluded_source_sha256=digest(hidden_path),binding_role='stationary upper-trouser seat supporting the pelvis')
        skin['parts'][seat_name]=seat_part;seat_parts.append(seat_name)
    skin['pelvis_structure']={'seat_parts':seat_parts,'description':'Upper trouser seat follows the waist; thighs articulate below the hip hinge.'}
    skin['bind_joints']={k:v.tolist() for k,v in points.items()};skin['reference_landmarks']=skin['bind_joints']
    atlas,positions=pack(images);skin['size']=list(atlas.size)
    for name,p in skin['parts'].items():
        im=images[name];x,y=positions[name];p['uv']=[x/atlas.width,1-(y+im.height)/atlas.height,im.width/atlas.width,im.height/atlas.height]
    hp=skin['parts']['head-neck'];skin['portrait']={'size':hp['size'],'uv':hp['uv']}
    atlaspath=DEST/(cid+'.png');atlas.save(atlaspath,optimize=True);texture_meta(atlaspath);skin['atlas_sha256']=digest(atlaspath)
    composite=render(skin,images,rig);composite.save(SOURCE/cid/'split-rest-composite.png')
    for folder,img in [('PaperReference',reference),('PaperBust',composite)]:
        dest=RESOURCE_ROOT/folder;dest.mkdir(exist_ok=True);p=dest/(cid+'.png');img.save(p);texture_meta(p)
    expected=np.array(reference)[:,:,3]>127;actual=np.array(composite)[:,:,3]>127
    skin['reference_comparison']={'silhouette_iou':float((actual&expected).sum()/max((actual|expected).sum(),1)),
        'note':'Compare proportions and assembly visually. Empty hands use a neutral bind pose instead of copying tool gestures; split drawings differ from the finished illustration.'}
    (SOURCE/cid/'split-binding.json').write_text(json.dumps(skin,ensure_ascii=False,indent=2,default=lambda x:x.item())+'\n',encoding='utf-8')
    return skin

def bake_split_cast():
    RESOURCE_ROOT.mkdir(parents=True,exist_ok=True);DEST.mkdir(exist_ok=True)
    manifest=json.loads((SOURCE/'manifest.json').read_text(encoding='utf-8'));cal=json.loads((SOURCE/'assembly-calibration.json').read_text(encoding='utf-8'))['characters']
    config=load_calibrations();rig=json.loads((ROOT/'unity/paper-rig.json').read_text(encoding='utf-8'))
    # Verify the requested archive, rather than silently consuming stale boards.
    import zipfile,hashlib
    archive=ROOT/'output/imagegen/puppet-cast24-modern-flat-20261026.zip'
    if archive.exists():
        with zipfile.ZipFile(archive) as z:
            for c in manifest['characters']:
                data=z.read('puppet-cast24-modern-flat-20261026/'+c['file'])
                if hashlib.sha256(data).hexdigest()!=digest(SOURCE/c['file']):raise ValueError('Archive/source mismatch: '+c['id'])
    for c in manifest['characters']:
        if digest(SOURCE/c['file'])!=c['sha256']:raise ValueError('Manifest/source mismatch: '+c['id'])
    from extract_cast_references import extract_references
    extract_references(manifest,cal)
    result=dict(version=7,collection='modern-flat-20261026',binding_basis='independent exploded component panels',source_archive_sha256=digest(archive) if archive.exists() else None,
        reference_role='visual comparison only; actors use independent split panels',equipment_policy='no hand-held objects',source_archive=archive.name,expected_skins=24,rig=rig,skins={})
    for c in manifest['characters']:
        ref=Image.open(SOURCE/c['id']/'assembly-reference.png').convert('RGBA')
        if cal[c['id']]['mirror']:ref=ref.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        result['skins'][c['id']]=build_skin(c,cal[c['id']],config[c['id']],rig,ref);print(c['id'],flush=True)
    result['expected_parts']=sum(len(s['parts']) for s in result['skins'].values())
    (DEST/'index.json').write_text(json.dumps(result,ensure_ascii=False,indent=2,default=lambda x:x.item())+'\n',encoding='utf-8');return result

if __name__=='__main__':bake_split_cast()
