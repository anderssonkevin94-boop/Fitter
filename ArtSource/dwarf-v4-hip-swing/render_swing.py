"""Render a five-frame, eight-facing rigid 2D walk from matched painted parts."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps, ImageFont
import numpy as np
import math,json
R=Path(__file__).resolve().parent
BASE=['N','NE','E','SE','S'];ORDER=['N','NE','E','SE','S','SW','W','NW']
CFG=json.loads((R/'extraction.json').read_text())
MASTER=512;CELL=256;COUNT=5;DURATION=90
import os
GROUND=(255.5,444);HEAD_Y=40;STRIDE=float(os.environ.get('STRIDE','40'));LIFT=float(os.environ.get('LIFT','12'));ARM=float(os.environ.get('ARM','1'))
# Floor points under the two ankles, measured in the uncalibrated standing art.
FLOOR={'N':[(199,469),(315,469)],'NE':[(202,430),(276,469)],'E':[(243,406),(235,476)],'SE':[(303,428),(204,469)],'S':[(189,465),(312,465)]}
ANATOMY={'N':['left','right'],'NE':['left','right'],'E':['left','right'],'SE':['left','right'],'S':['right','left']}
ARMS={'N':['left','right'],'NE':['left','right'],'E':['right','left'],'SE':['left','right'],'S':['right','left']}
try:
 font=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf',22)
except OSError:
 font=ImageFont.load_default(size=22)

def affine(im,matrix):
 return im.convert('RGBa').transform((MASTER,MASTER),Image.Transform.AFFINE,matrix,Image.Resampling.BICUBIC).convert('RGBA')
def translate(im,x,y):return affine(im,(1,0,-x,0,1,-y))
def rotate(im,pivot,angle,bob=0):
 a=math.radians(angle);c=math.cos(a);s=math.sin(a);px,py=pivot
 return affine(im,(c,s,px-c*px-s*(py+bob),-s,c,py+s*px-c*(py+bob)))
def gait(t):
 t=t%1
 if t<.6:return 1-t/.3,0,True
 u=(t-.6)/.4
 return -math.cos(math.pi*u),math.sin(math.pi*u),False

def calibrate(n):
 floor=FLOOR[n];gx=sum(p[0] for p in floor)/2;gy=sum(p[1] for p in floor)/2
 scale=(GROUND[1]-HEAD_Y)/(gy-32)
 dx=GROUND[0]-gx*scale;dy=GROUND[1]-gy*scale
 return scale,dx,dy

def point(n,p):
 scale,dx,dy=calibrate(n);return (p[0]*scale+dx,p[1]*scale+dy)

parts={};rig={};frames={};motions={};neutral={}
for n in BASE:
 cfg=CFG[n];s,dx,dy=calibrate(n);p={}
 for file in (R/'parts'/n).glob('*.png'):
  p[file.stem]=affine(Image.open(file).convert('RGBA'),(1/s,0,-dx/s,0,1/s,-dy/s))
 # Complete source legs include hidden thigh paint. Limit the upper leg to
 # its hip socket so a longer translation cannot expose it behind the torso.
 if n=='E':
  for j in [0,1]:
   leg=p[f'leg-{j}'];a=np.array(leg)
   cx,cy=point(n,((243 if j==0 else 238),320))
   rx=(40 if j==0 else 49)*s;ry=30*s
   yy,xx=np.mgrid[:MASTER,:MASTER]
   top=cy-ry*np.sqrt(np.maximum(0,1-((xx-cx)/rx)**2))
   a[:,:,3]=(a[:,:,3]*np.clip(yy-top+.5,0,1)).astype(np.uint8)
   p[f'leg-{j}']=Image.fromarray(a)
 parts[n]=p
 rig[n]={'sourceScale':s,'sourceOffset':[dx,dy],'groundOrigin':GROUND,'armPivots':[point(n,q) for q in cfg['piv']],'floorPoints':[point(n,q) for q in FLOOR[n]],'armSides':ARMS[n],'legSides':ANATOMY[n],'direction':cfg['vec']}
 dest=R/'rig-parts'/n;dest.mkdir(parents=True,exist_ok=True)
 for key,im in p.items():
  name='torso' if key=='body' else key.split('-')[0]+'-'+(ARMS[n] if key.startswith('arm') else ANATOMY[n])[int(key[-1])]
  im.save(dest/f'{name}.png')
  cropped=R/'cropped-parts'/n;cropped.mkdir(parents=True,exist_ok=True)
  box=im.getbbox();im.crop(box).save(cropped/f'{name}.png')
  rig[n].setdefault('parts',{})[name]={'file':f'cropped-parts/{n}/{name}.png','offset':[box[0],box[1]],'size':[box[2]-box[0],box[3]-box[1]],'fullyOccluded':n=='E' and name=='arm-left'}

def leg_swing(im,rest,out,fx,fy,theta,short):
 """A rigid leg rotated about its hip: the image's vertical axis (hip to foot) swings forward by theta (the forward
 part projects as fx sideways and half of fy up or down the screen), its horizontal axis tilts with it in profile;
 short < 1 bends the knee a little (the swinging foot clears the floor)."""
 s=math.sin(theta);c=math.cos(theta)
 hx=1-abs(fx)*(1-c);hy=-fx*s
 vx=fx*s*short;vy=(c+.5*fy*s)*short
 det=hx*vy-vx*hy;ia=vy/det;ib=-vx/det;idd=-hy/det;ie=hx/det
 return affine(im,(ia,ib,rest[0]-ia*out[0]-ib*out[1],idd,ie,rest[1]-idd*out[0]-ie*out[1]))

def leg_length(n):
 p=parts[n];r=rig[n]
 return max(r['floorPoints'][j][1]-p[f'leg-{j}'].getbbox()[1] for j in [0,1])-20

def pose(n,t,still=False):
 p=parts[n];r=rig[n];fx,fy=r['direction']
 # Weight shifts toward the supporting leg without moving its floor point.
 phase=0 if still else math.sin(2*math.pi*t)
 lateral=1 if r['floorPoints'][0][0]>r['floorPoints'][1][0] else -1
 sway=[lateral*3.5*abs(fy)*phase,-1.75*abs(fx)*phase]
 roll=lateral*.9*abs(fy)*phase
 hip=(GROUND[0],point(n,(0,350))[1])
 L=leg_length(n)
 legs=[]
 for j in [0,1]:
  travel,lift,planted=(0,0,True) if still else gait(t+j*.5)
  theta=math.asin(max(-.95,min(.95,travel*STRIDE/L)))
  legs.append((travel,lift,planted,theta))
 # The hips ride on the supporting leg: highest over a vertical leg, lower at the ends of the stride.
 drops=[L*(1-math.cos(th)) for tr,li,pl,th in legs if pl]
 bob=min(drops) if drops else 0
 out=Image.new('RGBA',(MASTER,MASTER));record={'legs':[],'arms':[],'bodyBob':bob,'bodySway':sway,'bodyRoll':roll,'bodyPivot':hip,'legLength':L}
 for j in [0,1]:
  travel,lift,planted,theta=legs[j]
  floor=r['floorPoints'][j]
  rest=(floor[0],floor[1]-L)
  pivot=(rest[0],rest[1]+bob)
  out.alpha_composite(leg_swing(p[f'leg-{j}'],rest,pivot,fx,fy,theta,1-.12*lift))
  foot=(pivot[0]+fx*L*math.sin(theta),pivot[1]+(math.cos(theta)+.5*fy*math.sin(theta))*L*(1-.12*lift))
  record['legs'].append({'side':r['legSides'][j],'hipPivot':pivot,'angle':math.degrees(theta),'lift':lift,'planted':planted,'floorPoint':foot})
 upper=p['body'].copy()
 for j,pivot in enumerate(r['armPivots']):
  legj=j if n!='E' else 1
  travel=0 if still else gait(t+legj*.5)[0]
  angle=(fx*6+fy*1.2)*travel*ARM
  if n in ['NE','SE'] and j==0:angle*=.35
  upper.alpha_composite(rotate(p[f'arm-{j}'],pivot,angle))
  a=math.radians(roll);c=math.cos(a);ss=math.sin(a)
  px=hip[0]+c*(pivot[0]-hip[0])-ss*(pivot[1]-hip[1])+sway[0]
  py=hip[1]+ss*(pivot[0]-hip[0])+c*(pivot[1]-hip[1])+sway[1]+bob
  record['arms'].append({'side':r['armSides'][j],'pivot':[px,py],'angle':angle+roll,'localAngle':angle})
 # Arms and torso share one final transform, preserving shoulder attachment.
 out.alpha_composite(translate(rotate(upper,hip,roll),sway[0],sway[1]+bob))
 return out,record

for n in BASE:
 frames[n]=[];motions[n]=[]
 for i in range(COUNT):
  im,record=pose(n,i/COUNT);frames[n].append(im);motions[n].append(record)
 neutral[n]=pose(n,0,True)[0]
for dst,src in [('W','E'),('NW','NE'),('SW','SE')]:
 frames[dst]=[ImageOps.mirror(f) for f in frames[src]];neutral[dst]=ImageOps.mirror(neutral[src])
 rr=json.loads(json.dumps(rig[src]));rr['mirroredFrom']=src;rr['direction'][0]*=-1;rr['parts']={}
 # Pixel centers mirror around (width - 1)/2, including the origin and anchors.
 rr['groundOrigin']=[MASTER-1-GROUND[0],GROUND[1]]
 rr['floorPoints']=[[MASTER-1-x,y] for x,y in rr['floorPoints']]
 rr['armPivots']=[[MASTER-1-x,y] for x,y in rr['armPivots']]
 rr['armSides']=['right' if s=='left' else 'left' for s in rr['armSides']]
 rr['legSides']=['right' if s=='left' else 'left' for s in rr['legSides']]
 rig[dst]=rr;motions[dst]=[]
 for record in motions[src]:
  rec=json.loads(json.dumps(record))
  rec['bodySway'][0]*=-1;rec['bodyRoll']*=-1;rec['bodyPivot'][0]=MASTER-1-rec['bodyPivot'][0]
  for leg in rec['legs']:
   leg['side']='right' if leg['side']=='left' else 'left';leg['angle']*=-1;leg['hipPivot']=[MASTER-1-leg['hipPivot'][0],leg['hipPivot'][1]];leg['floorPoint']=[MASTER-1-leg['floorPoint'][0],leg['floorPoint'][1]]
  for arm in rec['arms']:
   arm['side']='right' if arm['side']=='left' else 'left';arm['pivot'][0]=MASTER-1-arm['pivot'][0];arm['angle']*=-1;arm['localAngle']*=-1
  motions[dst].append(rec)
 dest=R/'rig-parts'/dst;dest.mkdir(parents=True,exist_ok=True)
 cropped=R/'cropped-parts'/dst;cropped.mkdir(parents=True,exist_ok=True)
 for f in (R/'rig-parts'/src).glob('*.png'):
  name=f.stem.replace('left','TEMP').replace('right','left').replace('TEMP','right');im=ImageOps.mirror(Image.open(f));im.save(dest/f'{name}.png');box=im.getbbox();im.crop(box).save(cropped/f'{name}.png')
  rr['parts'][name]={'file':f'cropped-parts/{dst}/{name}.png','offset':[box[0],box[1]],'size':[box[2]-box[0],box[3]-box[1]],'fullyOccluded':dst=='W' and name=='arm-right'}

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
