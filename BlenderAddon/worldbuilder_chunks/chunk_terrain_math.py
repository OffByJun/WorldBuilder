"""Deterministic global-coordinate terrain height functions."""
import math

def _hash(ix,iy,seed):
    value=(ix*0x1F123BB5)^(iy*0x5F356495)^(seed*0x6C8E9CF5);value&=0xffffffff
    value^=value>>16;value=(value*0x7feb352d)&0xffffffff;value^=value>>15;value=(value*0x846ca68b)&0xffffffff;value^=value>>16
    return value/4294967295.0*2.0-1.0
def _smooth(t):return t*t*(3-2*t)
def _noise(x,y,seed):
    x0=math.floor(x);y0=math.floor(y);tx=_smooth(x-x0);ty=_smooth(y-y0)
    a=_hash(x0,y0,seed)*(1-tx)+_hash(x0+1,y0,seed)*tx;b=_hash(x0,y0+1,seed)*(1-tx)+_hash(x0+1,y0+1,seed)*tx
    return a*(1-ty)+b*ty
def fbm(x,y,seed,octaves=5,persistence=.52):
    total=0.0;amplitude=1.0;frequency=1.0;normalizer=0.0
    for octave in range(max(1,int(octaves))):
        total+=_noise(x*frequency,y*frequency,seed+octave*131)*amplitude;normalizer+=amplitude;amplitude*=persistence;frequency*=2.0
    return total/max(normalizer,1e-9)
def height(world_x,world_y,seed,feature_size,relief,base_height,preset="REEF_PLAINS"):
    scale=max(float(feature_size),.001);value=fbm(world_x/scale,world_y/scale,int(seed))
    if preset=="RIDGED":value=1.0-abs(value);value=value*2.0-1.0
    elif preset=="CANYON":value=-max(0.0,1.0-abs(value)*3.2)+fbm(world_x/(scale*2),world_y/(scale*2),seed+991)*.25
    elif preset=="PLATEAU":value=round(value*5.0)/5.0
    return float(base_height)+float(relief)*value

def chunk_vertices(coord,origin,chunk_size,cells,settings):
    cells=max(1,int(cells));step=float(chunk_size)/cells;ox=float(origin[0])+coord[0]*chunk_size;oy=float(origin[1])+coord[1]*chunk_size
    return [(ix*step,iy*step,height(ox+ix*step,oy+iy*step,settings["seed"],settings["feature_size"],settings["relief"],settings["base_height"],settings.get("preset","REEF_PLAINS"))) for iy in range(cells+1) for ix in range(cells+1)]

def chunk_faces(coord,cells):
    faces=[];stride=cells+1
    for iy in range(cells):
        for ix in range(cells):
            a=iy*stride+ix;b=a+1;c=a+stride;d=c+1
            if ((coord[0]*cells+ix)+(coord[1]*cells+iy))&1:faces.extend(((a,b,c),(b,d,c)))
            else:faces.extend(((a,b,d),(a,d,c)))
    return faces
