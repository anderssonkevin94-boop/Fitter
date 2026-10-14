"""Five-frame 2D walk using rigid thigh, shin and foot pieces."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageOps,ImageFont
import numpy as np,math,json
R=Path(__file__).resolve().parent;CFG=json.loads((R/'extraction.json').read_text())
BASE=['N','NE','E','SE','S'];ORDER=['N','NE','E','SE','S','SW','W','NW']
import os
MASTER=512;CELL=256;COUNT=5;DURATION=90;STRIDE=float(os.environ.get('STRIDE','40'));LIFT=float(os.environ.get('LIFT','16'))
GROUND=(255.5,444);HEAD_Y=40
try:font=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf',22)
except OSError:font=ImageFont.load_default(size=22)
def affine(im,m):return im.convert('RGBa').transform((512,512),Image.Transform.AFFINE,m,Image.Resampling.BICUBIC).convert('RGBA')
def transform(im,p,q,angle=0):
 a=math.radians(angle);c=math.cos(a);s=math.sin(a)
 return affine(im,(c,s,p[0]-c*q[0]-s*q[1],-s,c,p[1]+s*q[0]-c*q[1]))
def rotatedpoint(p,pivot,shift,angle):
 a=math.radians(angle);c=math.cos(a);s=math.sin(a);x=p[0]-pivot[0];y=p[1]-pivot[1]
 return [pivot[0]+x*c-y*s+shift[0],pivot[1]+x*s+y*c+shift[1]]
def gait(t):
 t%=1
 if t<.6:return 1-t/.3,0,True
 u=(t-.6)/.4;return -math.cos(math.pi*u),math.sin(math.pi*u),False
parts={};rig={};frames={};motions={};neutral={}
for n in BASE:
 cfg=CFG[n];gx=sum(x for x,y in cfg['floor'])/2;gy=sum(y for x,y in cfg['floor'])/2
 scale=(444-40)/(gy-28);dx=255.5-gx*scale;dy=444-gy*scale
 point=lambda p:[p[0]*scale+dx,p[1]*scale+dy]
 parts[n]={p.stem:affine(Image.open(p).convert('RGBA'),(1/scale,0,-dx/scale,0,1/scale,-dy/scale)) for p in (R/'parts'/n).glob('*.png')}
 rig[n]={'sourceScale':scale,'groundOrigin':list(GROUND),'direction':cfg['vec'],'legSides':cfg['sides'],'joints':[{k:point(v) for k,v in j.items()} for j in cfg['joints']],'parts':{}}
 neutral[n]=affine(Image.open(R/'source'/f'{n}.png').convert('RGBA'),(1/scale,0,-dx/scale,0,1/scale,-dy/scale))
 for key,im in parts[n].items():
  name=key if key=='upper-body' else key.rsplit('-',1)[0]+'-'+cfg['sides'][int(key[-1])]
  for folder in ['rig-parts','cropped-parts']:(R/folder/n).mkdir(parents=True,exist_ok=True)
  im.save(R/'rig-parts'/n/f'{name}.png');box=im.getbbox();im.crop(box).save(R/'cropped-parts'/n/f'{name}.png')
  rig[n]['parts'][name]={'file':f'cropped-parts/{n}/{name}.png','offset':list(box[:2]),'size':[box[2]-box[0],box[3]-box[1]]}
NARROW=float(os.environ.get('NARROW','0.35'))
KNEE=float(os.environ.get('KNEE','30'))
def place(im,rest,out,M):
 """Draws a part so its rest point lands on out, transformed by the 2x2 matrix M (columns: image x and y axes)."""
 (hx,vx),(hy,vy)=M;det=hx*vy-vx*hy;ia=vy/det;ib=-vx/det;idd=-hy/det;ie=hx/det
 return affine(im,(ia,ib,rest[0]-ia*out[0]-ib*out[1],idd,ie,rest[1]-idd*out[0]-ie*out[1]))
def mul(A,B):return ((A[0][0]*B[0][0]+A[0][1]*B[1][0],A[0][0]*B[0][1]+A[0][1]*B[1][1]),(A[1][0]*B[0][0]+A[1][1]*B[1][0],A[1][0]*B[0][1]+A[1][1]*B[1][1]))
def app(M,v):return [M[0][0]*v[0]+M[0][1]*v[1],M[1][0]*v[0]+M[1][1]*v[1]]
def pose(n,t):
 """Each leg swings as one piece from its hip (thigh, shin and foot together); the swinging leg bends at the knee to
 clear the floor; the hips ride on the supporting leg. Toward or away from the camera the swing foreshortens. The legs
 walk on a narrower track than the standing pose (NARROW) so the front and back views don't straddle."""
 p=parts[n];r=rig[n];fx,fy=r['direction']
 phase=math.sin(2*math.pi*t)
 side=1 if r['joints'][0]['floor'][0]>r['joints'][1]['floor'][0] else -1
 sway=[side*2.0*abs(fy)*phase,-1.0*abs(fx)*phase];roll=side*.5*abs(fy)*phase;pivot=[255.5,330]
 legs=[]
 for j,rest in enumerate(r['joints']):
  travel,lift,planted=gait(t+j*.5)
  L=math.hypot(rest['ankle'][0]-rest['hip'][0],rest['ankle'][1]-rest['hip'][1])
  th=math.asin(max(-.9,min(.9,travel*STRIDE/L)))
  legs.append((rest,travel,lift,planted,th,L))
 drops=[L*(1-math.cos(th)) for rest,tr,li,pl,th,L in legs if pl]
 bob=min(drops) if drops else 0
 shift=[sway[0],sway[1]+bob]
 out=Image.new('RGBA',(512,512));rec={'bodyBob':bob,'bodySway':sway,'bodyRoll':roll,'bodyPivot':pivot,'legs':[]}
 for j,(rest,travel,lift,planted,th,L) in enumerate(legs):
  s_=math.sin(th);c=math.cos(th)
  M=((1-abs(fx)*(1-c),fx*s_),(-fx*s_,c+.5*fy*s_))      # whole-leg swing about the hip
  nx=-NARROW*abs(fy)*(rest['hip'][0]-255.5)             # a narrower walking track (front and back views)
  H=[rest['hip'][0]+nx,rest['hip'][1]+bob]
  K=[H[k]+app(M,[rest['knee'][0]-rest['hip'][0],rest['knee'][1]-rest['hip'][1]])[k] for k in [0,1]]
  # Knee bend while the leg swings: the shin folds back (in profile) or foreshortens (toward/away from camera).
  b=math.radians(KNEE*lift*abs(fx))*(-fx if fx else 1)
  R=((math.cos(b),-math.sin(b)),(math.sin(b),math.cos(b)))
  Sh=((1,0),(0,1-.3*lift*abs(fy)))
  Ms=mul(R,mul(Sh,M))
  A=[K[k]+app(Ms,[rest['ankle'][0]-rest['knee'][0],rest['ankle'][1]-rest['knee'][1]])[k] for k in [0,1]]
  out.alpha_composite(place(p[f'upper-leg-{j}'],rest['hip'],H,M))
  out.alpha_composite(place(p[f'lower-leg-{j}'],rest['knee'],K,Ms))
  out.alpha_composite(transform(p[f'foot-{j}'],rest['ankle'],A,0))
  floor=[A[0]+rest['floor'][0]-rest['ankle'][0],A[1]+rest['floor'][1]-rest['ankle'][1]]
  rec['legs'].append({'side':r['legSides'][j],'hip':H,'knee':K,'ankle':A,'upperAngle':math.degrees(th),'lowerAngle':math.degrees(th+b),'footAngle':0,'lift':lift,'planted':planted,'floorPoint':floor,'reachError':0})
 out.alpha_composite(transform(p['upper-body'],pivot,[pivot[k]+shift[k] for k in [0,1]],roll))
 return out,rec
for n in BASE:
 frames[n]=[];motions[n]=[]
 for i in range(COUNT):
  im,rec=pose(n,i/COUNT);frames[n].append(im);motions[n].append(rec)
for dst,src in [('W','E'),('NW','NE'),('SW','SE')]:
 frames[dst]=[ImageOps.mirror(f) for f in frames[src]];neutral[dst]=ImageOps.mirror(neutral[src]);rr=json.loads(json.dumps(rig[src]));rr['mirroredFrom']=src;rr['direction'][0]*=-1;rr['parts']={}
 rr['legSides']=['right' if s=='left' else 'left' for s in rr['legSides']]
 for j in rr['joints']:
  for point in j.values():point[0]=511-point[0]
 rig[dst]=rr;motions[dst]=[]
 for record in motions[src]:
  rec=json.loads(json.dumps(record));rec['bodySway'][0]*=-1;rec['bodyRoll']*=-1;rec['bodyPivot'][0]=511-rec['bodyPivot'][0]
  for leg in rec['legs']:
   leg['side']='right' if leg['side']=='left' else 'left'
   for k in ['hip','knee','ankle','floorPoint']:leg[k][0]=511-leg[k][0]
   for k in ['upperAngle','lowerAngle','footAngle']:leg[k]*=-1
  motions[dst].append(rec)
 for f in (R/'rig-parts'/src).glob('*.png'):
  name=f.stem.replace('left','TEMP').replace('right','left').replace('TEMP','right');im=ImageOps.mirror(Image.open(f));box=im.getbbox()
  for folder in ['rig-parts','cropped-parts']:(R/folder/dst).mkdir(parents=True,exist_ok=True)
  im.save(R/'rig-parts'/dst/f'{name}.png');im.crop(box).save(R/'cropped-parts'/dst/f'{name}.png');rr['parts'][name]={'file':f'cropped-parts/{dst}/{name}.png','offset':list(box[:2]),'size':[box[2]-box[0],box[3]-box[1]]}

# Center on the pixel symmetry axis so all mirrored views share the same floor origin.
meta={'image':'dwarf-walk.png','frameSize':[CELL,CELL],'origin':[128,222],'frameCount':COUNT,'frameDurationMs':DURATION,'loopDurationMs':COUNT*DURATION,'directionOrder':ORDER,'animations':{}}
ase={'frames':{},'meta':{'app':'Dwarf 2D cutout renderer','image':'dwarf-walk.png','format':'RGBA8888','size':{'w':CELL*COUNT,'h':CELL*len(ORDER)},'scale':'1','frameTags':[]}}
atlas=Image.new('RGBA',(CELL*COUNT,CELL*len(ORDER)));allsmall={}
for row,n in enumerate(ORDER):
 fd=R/'frames'/n;fd.mkdir(parents=True,exist_ok=True)
 masterdir=R/'master-frames'/n;masterdir.mkdir(parents=True,exist_ok=True)
 small=[]
 for i,im in enumerate(frames[n]):
  im.save(masterdir/f'{i:02}.png');f=im.resize((CELL,CELL),Image.Resampling.LANCZOS);f.save(fd/f'{i:02}.png');small.append(f);atlas.paste(f,(i*CELL,row*CELL))
  ase['frames'][f'{n}-{i}']={'frame':{'x':i*CELL,'y':row*CELL,'w':CELL,'h':CELL},'rotated':False,'trimmed':False,'spriteSourceSize':{'x':0,'y':0,'w':CELL,'h':CELL},'sourceSize':{'w':CELL,'h':CELL},'duration':DURATION}
 allsmall[n]=small
 (R/'standing').mkdir(exist_ok=True);neutral[n].resize((CELL,CELL),Image.Resampling.LANCZOS).save(R/'standing'/f'{n}.png')
 strip=Image.new('RGBA',(CELL*COUNT,CELL))
 for i,f in enumerate(small):strip.paste(f,(i*CELL,0))
 (R/'sheets').mkdir(exist_ok=True);strip.save(R/'sheets'/f'{n}.png')
 (R/'animations').mkdir(exist_ok=True);small[0].save(R/'animations'/f'{n}.webp',save_all=True,append_images=small[1:],duration=DURATION,loop=0,lossless=True)
 flat=[]
 for f in small:
  bg=Image.new('RGBA',(CELL,CELL),'#e7e4de');bg.alpha_composite(f);flat.append(bg.convert('RGB'))
 # One palette across the whole loop prevents GIF color flicker.
 palettecanvas=Image.new('RGB',(CELL*COUNT,CELL),'#e7e4de')
 for i,f in enumerate(flat):palettecanvas.paste(f,(CELL*i,0))
 pal=palettecanvas.quantize(colors=256);flat=[f.quantize(palette=pal,dither=Image.Dither.NONE) for f in flat]
 flat[0].save(R/'animations'/f'{n}.gif',save_all=True,append_images=flat[1:],duration=DURATION,loop=0,disposal=2,optimize=False)
 fx,fy=rig[n]['direction'];distance=STRIDE/.3/2
 meta['animations'][n]={'row':row,'frames':[f'{n}-{i}' for i in range(COUNT)],'direction':[fx,fy],'screenMotionPerCycle':[fx*distance,fy*distance*.5],'screenVelocityPerSecond':[fx*distance/(COUNT*DURATION/1000),fy*distance*.5/(COUNT*DURATION/1000)]}
 ase['meta']['frameTags'].append({'name':n,'from':row*COUNT,'to':row*COUNT+COUNT-1,'direction':'forward'})
atlas.save(R/'dwarf-walk.png')
(R/'sprite-data.json').write_text(json.dumps(meta,indent=2));(R/'dwarf-walk.json').write_text(json.dumps(ase,indent=2));(R/'rig.json').write_text(json.dumps(rig,indent=2));(R/'motion.json').write_text(json.dumps(motions,indent=2))
# Inspection outputs: full-size frame strips on two backgrounds and detail crops.
checks=R/'checks';checks.mkdir(exist_ok=True)
for n in ORDER:
 contact=Image.new('RGB',(CELL*6,576),'#e7e4de');d=ImageDraw.Draw(contact)
 for j,i in enumerate([0,1,2,3,4,0]):
  f=allsmall[n][i];contact.paste(f,(j*CELL,24),f)
  dark=Image.new('RGBA',(CELL,CELL),'#242824');dark.alpha_composite(f);contact.paste(dark.convert('RGB'),(j*CELL,312))
  d.text((j*CELL+12,282),f'{n} · {i+1}',font=font,fill='#242824')
 contact.save(checks/f'{n}-frames.png')
 detail=Image.new('RGB',(MASTER*5,MASTER),'#e7e4de')
 for i,f in enumerate(frames[n]):detail.paste(f,(i*MASTER,0),f)
 detail.save(checks/f'{n}-master.png')
overview=[]
for i in range(COUNT):
 bg=Image.new('RGB',(1024,576),'#e7e4de');d=ImageDraw.Draw(bg)
 for j,n in enumerate(ORDER):
  x=j%4*CELL;y=j//4*288;f=allsmall[n][i];bg.paste(f,(x,y),f);d.text((x+111,y+257),n,font=font,fill='#292722')
 overview.append(bg)
palcanvas=Image.new('RGB',(1024,576*COUNT))
for i,f in enumerate(overview):palcanvas.paste(f,(0,576*i))
pal=palcanvas.quantize(colors=256);over=[f.quantize(palette=pal,dither=Image.Dither.NONE) for f in overview]
over[0].save(R/'all-directions.gif',save_all=True,append_images=over[1:],duration=DURATION,loop=0,disposal=2,optimize=False)
overview[0].save(R/'all-directions.png')
print('Rendered 40 frames with a shared ground origin, 8 strips, atlas, animations, and import metadata.')
