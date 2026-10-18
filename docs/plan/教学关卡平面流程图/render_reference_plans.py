"""Export dimension-preserving XZ diagrams from the Unity reference manifests.

No scene is modified.  Bounds are projected with Rz*Ry*Rx (qz*qy*qx).
Routes are annotated intended interactions, not a playthrough/collision result.
"""
from pathlib import Path
from itertools import product
import math
import json
import html
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[2]
DATA = ROOT / 'UnityProject/PAO/Assets/GameMain/Data/ShrineReferences'
FONT = 'C:/Windows/Fonts/msyh.ttc'
W, H = 3200, 2250
BG = '#edf1f0'
INK = '#173e46'
MUTED = '#58757a'
LINE = '#c9d5d5'
MAIN = '#057fb8'
OPTION = '#c47a19'
MOTION = '#7654af'
MECH = '#50b4ac'
BREAK = '#dd7e71'

def f(s):
    return ImageFont.truetype(FONT, s)

def tx(d, p, value, size=25, fill=INK, anchor=None):
    d.text(p, value, font=f(size), fill=fill, anchor=anchor, spacing=8)

def roundbox(d, b, fill='white', edge=LINE, r=20):
    d.rounded_rectangle(b, r, fill=fill, outline=edge, width=2)

def xyz(v):
    return tuple(v[k] for k in 'xyz')

def transform(o, v):
    x, y, z = (v[i] * xyz(o['scale'])[i] for i in range(3))
    rx, ry, rz = (math.radians(a) for a in xyz(o['rotation']))
    y, z = y*math.cos(rx)-z*math.sin(rx), y*math.sin(rx)+z*math.cos(rx)
    x, z = x*math.cos(ry)+z*math.sin(ry), -x*math.sin(ry)+z*math.cos(ry)
    x, y = x*math.cos(rz)-y*math.sin(rz), x*math.sin(rz)+y*math.cos(rz)
    return tuple(a+b for a,b in zip((x,y,z),xyz(o['position'])))

def corners(o):
    return [transform(o,v) for v in product(*zip(xyz(o['boundsMin']),xyz(o['boundsMax'])))]

def hull(points):
    points = sorted(set(points))
    def cross(o,a,b): return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    if len(points)<3: return points
    lo,hi=[],[]
    for p in points:
        while len(lo)>=2 and cross(lo[-2],lo[-1],p)<=0: lo.pop()
        lo.append(p)
    for p in reversed(points):
        while len(hi)>=2 and cross(hi[-2],hi[-1],p)<=0: hi.pop()
        hi.append(p)
    return lo[:-1]+hi[:-1]

class Plan:
    def __init__(self,d,bounds,rect):
        self.d=d
        self.xmin,self.zmin,self.xmax,self.zmax=bounds
        l,t,r,b=rect
        # Same pixels per source unit in both sheets, preserving X:Z proportions.
        self.s=min(9.5,(r-l)/(self.xmax-self.xmin),(b-t)/(self.zmax-self.zmin))
        self.l=l+((r-l)-(self.xmax-self.xmin)*self.s)/2
        self.t=t+((b-t)-(self.zmax-self.zmin)*self.s)/2
    def p(self,x,z): return (self.l+(x-self.xmin)*self.s,self.t+(z-self.zmin)*self.s)
    def line(self,points,color=MAIN,width=7,dashed=False,arrow=True):
        pp=[self.p(*p) for p in points]
        line(self.d,pp,color,width,dashed,arrow)
    def poly(self,points,fill,edge):
        pp=[self.p(*p) for p in points]
        if len(pp)>2: self.d.polygon(pp,fill=fill,outline=edge,width=1)
    def badge(self,point,label,dx=0,dy=0,color=INK):
        p=self.p(*point); q=(p[0]+dx,p[1]+dy)
        if dx or dy: self.d.line([p,q],fill=color,width=2)
        r=22 if len(label)<3 else 29
        self.d.ellipse((q[0]-r,q[1]-r,q[0]+r,q[1]+r),fill=color,outline='white',width=3)
        tx(self.d,q,label,22,'white','mm')
    def tag(self,point,label,dx=0,dy=0,color=MUTED,size=22):
        p=self.p(*point); p=(p[0]+dx,p[1]+dy)
        bb=self.d.textbbox((0,0),label,font=f(size)); tw=bb[2]-bb[0]
        self.d.rounded_rectangle((p[0]-6,p[1]-5,p[0]+tw+10,p[1]+size+10),6,fill='#fffffff0')
        tx(self.d,p,label,size,color)
    def grid(self):
        d=self.d
        for x in range(math.ceil(self.xmin/10)*10,int(self.xmax)+1,10):
            d.line([self.p(x,self.zmin),self.p(x,self.zmax)],fill='#e6eded',width=1)
            tx(d,(self.p(x,self.zmax)[0],self.p(x,self.zmax)[1]+20),str(x),18,MUTED,'mt')
        for z in range(math.ceil(self.zmin/10)*10,int(self.zmax)+1,10):
            d.line([self.p(self.xmin,z),self.p(self.xmax,z)],fill='#e6eded',width=1)
            tx(d,(self.p(self.xmin,z)[0]-15,self.p(self.xmin,z)[1]),str(z),18,MUTED,'rm')

def line(d,pp,color,width=6,dashed=False,arrow=True):
    for a,b in zip(pp,pp[1:]):
        dist=math.dist(a,b)
        if not dist: continue
        if dashed:
            for i in range(0,int(dist),22):
                s=i/dist; e=min(1,(i+12)/dist)
                d.line([(a[0]+(b[0]-a[0])*s,a[1]+(b[1]-a[1])*s),(a[0]+(b[0]-a[0])*e,a[1]+(b[1]-a[1])*e)],fill=color,width=width)
        else:
            d.line([a,b],fill='white',width=width+4)
            d.line([a,b],fill=color,width=width)
    if arrow and len(pp)>1:
        a,b=pp[-2:]; ang=math.atan2(b[1]-a[1],b[0]-a[0]); r=18
        d.polygon([b,(b[0]-r*math.cos(ang-.5),b[1]-r*math.sin(ang-.5)),(b[0]-r*math.cos(ang+.5),b[1]-r*math.sin(ang+.5))],fill=color)

def plot_objects(p,layout):
    objs=layout['objects']
    def sortval(o): return (o['group'] in ('Mechanisms','Breakable','Landmarks'), max(v[1] for v in corners(o)))
    counts={'shown':0,'hidden':0}
    for o in sorted(objs,key=sortval):
        n=o['name']; kind=o['kind']; group=o['group']; cc=corners(o)
        # Strip the overhead covers and cosmetic trim for a readable cutaway.
        if group=='Detail' and not any(x in n for x in ('Road','SliderRail')):
            counts['hidden']+=1; continue
        if group=='Structure' and ('Ceiling' in n or ('BoxCover' in n and o['position']['y']>0)):
            counts['hidden']+=1; continue
        points=hull([(v[0],v[2]) for v in cc])
        if max(x for x,z in points)<p.xmin or min(x for x,z in points)>p.xmax or max(z for x,z in points)<p.zmin or min(z for x,z in points)>p.zmax: continue
        fill,edge='#dae3e2','#a9bbbd'
        if group=='Detail': fill,edge='#edf0d9','#bdc6ae'
        elif group=='Breakable': fill,edge=BREAK,'#995d58'
        elif group=='Mechanisms': fill,edge=MECH,'#2c7778'
        elif group=='Landmarks': fill,edge='#e6cd85','#9a8239'
        if kind=='spikes': fill,edge='#c981a4','#854467'
        if kind=='rope':
            p.line([(o['position']['x'],o['position']['z']),(o['endpoint']['x'],o['endpoint']['z'])],MOTION,2,True,False)
            continue
        if kind=='frame':
            p.poly(points,None,edge); continue
        if group=='Structure':
            ymax=max(v[1] for v in cc)
            # Higher enclosure blocks are outlines; they cannot mask the floor plan.
            cutoff=9 if layout['sceneName']=='Map2' else 0
            if min(v[1] for v in cc)>=cutoff:
                fill='#eef2f1'; edge='#cfdada'
        if kind in ('spikes','sphere','cylinder'):
            pp=[p.p(*point) for point in points]
            p.d.ellipse((min(v[0] for v in pp),min(v[1] for v in pp),max(v[0] for v in pp),max(v[1] for v in pp)),fill=fill,outline=edge,width=2)
        else:
            p.poly(points,fill,edge)
        if kind in ('stairs','slope'):
            a,b=o['boundsMin'],o['boundsMax']
            for k in range(1,6):
                z=a['z']+(b['z']-a['z'])*k/6
                va=transform(o,(a['x'],(a['y']+b['y'])/2,z)); vb=transform(o,(b['x'],(a['y']+b['y'])/2,z))
                p.line([(va[0],va[2]),(vb[0],vb[2])],'#8da6a8',1,False,False)
        counts['shown']+=1
    return counts

def header(d,num,title,sub):
    tx(d,(65,34),f'MAP {num}',40,'#3d8d8a')
    tx(d,(300,28),title,55)
    tx(d,(67,107),sub,26,MUTED)
    roundbox(d,(55,172,1135,2140))
    tx(d,(88,198),'剖顶平面  X–Z',30)
    tx(d,(1095,204),'−Z ↑',25,MUTED,'rt')
    roundbox(d,(1170,172,3145,2140))
    tx(d,(1208,198),'机关位置与通关流程',34)

def legend(d,x,y):
    tx(d,(x,y),'图例',29)
    for i,(color,label) in enumerate([(MAIN,'玩家主流程'),(OPTION,'可选支路'),(MOTION,'机关运动 / 投射'),(MECH,'可交互机关'),(BREAK,'可破坏物')]):
        xx=x+(i%3)*570; yy=y+62+(i//3)*54
        if i<3: line(d,[(xx,yy+13),(xx+65,yy+13)],color,5,i>0)
        else: d.rounded_rectangle((xx,yy,xx+65,yy+30),6,fill=color)
        tx(d,(xx+83,yy-5),label,25,MUTED)

def mechanism_rows(d,rows,x=1215,y=315,width=1870,step=112):
    for i,(num,title,coord,desc) in enumerate(rows):
        yy=y+i*step
        if i%2==0: d.rounded_rectangle((x-12,yy-8,x+width,yy+step-15),12,fill='#f1f5f4')
        d.ellipse((x,yy+8,x+48,yy+56),fill=INK)
        tx(d,(x+24,yy+32),num,23,'white','mm')
        tx(d,(x+75,yy),title,30)
        tx(d,(x+650,yy+2),coord,25,'#356f76')
        tx(d,(x+75,yy+48),desc,25,MUTED)

def footer(d,text):
    d.line([(120,2075),(310,2075)],fill=INK,width=3)
    d.line([(120,2067),(120,2083)],fill=INK,width=3)
    d.line([(310,2067),(310,2083)],fill=INK,width=3)
    tx(d,(120,2091),'20 原作单位 · 两图同一比例',22,MUTED)
    tx(d,(65,2178),'原坐标 / 1 原作单位 = 1 Unity 单位 · 物件包围尺寸投影 · '+text,23,MUTED)
    tx(d,(3130,2210),'2026.10.18',19,MUTED,'rt')

def small_note(d,x,y,lines):
    for i,s in enumerate(lines): tx(d,(x,y+i*42),s,24,MUTED)

def make_map2():
    layout=json.loads((DATA/'Map2_ReferenceLayout.json').read_text(encoding='utf-8-sig'))
    im=Image.new('RGB',(W,H),BG); d=ImageDraw.Draw(im)
    header(d,'2','加·巴希夫｜破坏之力','原始坐标重构图 · 炸墙 → 横向渡台 → 活塞弹射 → 右侧登高 → 折返祭坛')
    p=Plan(d,(-36,-124,36,34),(145,275,1080,2010)); p.grid(); stats=plot_objects(p,layout)
    # Main path is shown after the relevant blocker has been removed.
    p.line([(-18,25),(-18,13),(-18,0),(-18,-11),(-27.9,-11),(-27.9,-24),(-19,-28),(-4,-28),(4,-28),(16,-28),(16,-42),(18,-51.2)])
    p.line([(16,-42),(4,-42),(4,-64),(24,-64),(24,-74),(24,-88),(8,-89.5),(8,-108)])
    p.line([(-18,-11),(-6,-11),(-6,-4)],OPTION,5,True)
    p.line([(4,-44),(-23,-44),(-23,-53),(-23,-77)],OPTION,5,True)
    p.line([(-19,-28),(-9,-28)],MOTION,5,True); p.line([(-9,-29.3),(-19,-29.3)],MOTION,4,True)
    p.line([(-10,-53),(-10.5,-71)],MOTION,5,True); p.line([(-10.5,-71),(-10,-53)],MOTION,3,True)
    p.line([(24,-53.5),(24,-76)],MOTION,6,True)
    badges=[((-18,25),'S',0,0),((-26,11),'01',-48,0),((-18,0),'02',44,0),((-6,-4),'A',45,0),((-27.9,-14),'03',-36,-20),((-15,-28),'04',0,45),((-4,-28),'05',0,-40),((-23,-48),'06',-45,0),((-10,-50),'07',50,0),((18,-51.2),'08',-35,-40),((24,-80),'09',50,0),((8,-98),'10',50,0),((8,-111.5),'E',0,0)]
    for point,label,dx,dy in badges: p.badge(point,label,dx,dy)
    p.tag((-29,22),'入口台面 Y=2',15,-12)
    p.tag((-3,-20),'渡台层 Y≈4',15,-4)
    p.tag((-7,-61),'底层 Y=0',0,-5)
    p.tag((11,-91),'目标层 Y=6',20,-4)
    p.tag((-1,-116),'祭坛基座 Y=8',-90,-28)
    rows=[
      ('01','能力终端与入口坡','终端  (−26, 2.05, 11)','终端在入口左侧；坡面引导炸弹滚向第一道墙。'),
      ('02','首墙 → 横廊分支','首墙  (−20/−16, 0, 0)','2 块炸墙；右支路 A 的宝箱在 (−6, 0, −4)，主路向左转。'),
      ('03','左侧阻块与双梯','阻块 X=−27.9；Z=−12 / −16','2 块阻块；两处梯子将底层路径接到约 Y=4 的渡台层。'),
      ('04','6×6 炸弹渡台','轨道  (−19,4,−28) 至 (−9,4,−28)','沿 X 往返，单程 10；把炸弹送至对面后再渡过间隙。'),
      ('05','渡台目标炸墙','X=−4；Y=4 / 8；Z=−24 / −28 / −32','3 列×2 层，共 6 块。炸墙后进入上层 T 形分路。'),
      ('06','左侧人物弹射活塞','(−23,3,−46.5) → 落点 (−23,4,−77)','可选支路，弹射到宝箱平台；台下地面与末厅相通。'),
      ('07','相向弹球示范组','近侧 (−10,3,−46.5)；远侧 (−10.5,3,−77.5)','2 个活塞相向传球，加上 06 和 08，全厅共 4 个活塞。'),
      ('08','投弹漏管与右活塞','漏管 (18,8,−51.2)；活塞 (24,5,−47)','沿侧台登高投弹，弹射方向为 −Z，击破 09 石堆。'),
      ('09','右侧目标石堆 / 登高梯','石堆 X=20…28，Z=−76…−84；梯 (24,0,−74)','14 块石堆，底 Y=6；从底层沿梯上到目标平台。'),
      ('10','折返祭坛','(24,6,−89.5) → (8,6,−89.5) → (8,8,−111.5)','先向左横移，再转向 −Z，经台阶到终点。'),
    ]
    mechanism_rows(d,rows,step=125)
    legend(d,1220,1620)
    small_note(d,1220,1835,[
      '坐标括号采用 (X,Y,Z)。机关坐标为模型枢轴，台面高度另行标注。',
      '蓝线跨过炸块表示破坏后的流程；紫线是物体运动，不是玩家步行道路。',
      '已剖去天花装饰，保留真实布局比例；图中轮廓是模型包围盒投影。',
      '白盒机关行为、碰撞与本项目 5 秒飞行限制仍需实际调试。',
    ])
    footer(d,'来源：Dungeon041 / ZeldaMods radar；不是像素级原模型')
    path=OUT/'Map2_加巴希夫_原始坐标流程图.png'; im.save(path)
    print(path); print('Map2 projected modules:',stats)

def map3_profile(d):
    x0,y0=2045,1800; w,h=1000,255
    tx(d,(x0,y0-60),'四层滑块 · 侧面展开示意',28)
    tx(d,(x0,y0-20),'仅为解读上下层，不代表当前已拼成阶梯',21,MUTED)
    for i,top in enumerate([-15,-13,-11,-9]):
        xx=x0+80+i*210; yy=y0+180-i*38
        d.rounded_rectangle((xx,yy,xx+165,yy+32),6,fill=MECH,outline='#317d7c',width=2)
        tx(d,(xx+82,yy-8),f'Y={top}',23,INK,'mb')
        line(d,[(xx-25,yy+44),(xx+185,yy+44)],MOTION,3,True,True)
    tx(d,(x0+50,y0+250),'四块初始几乎叠在 X=0、Z≈−72；沿 Z 拉动形成高差路线。',20,MUTED)

def make_map3():
    layout=json.loads((DATA/'Map3_ReferenceLayout.json').read_text(encoding='utf-8-sig'))
    im=Image.new('RGB',(W,H),BG); d=ImageDraw.Draw(im)
    header(d,'3','摩·拉塔尼阿｜配合摇晃','原始坐标重构图 · 两块摆台 → 木吊桥 → 四颗刺球 → 斜轨 / 四层滑块 → 吊灯与门')
    p=Plan(d,(-36,-113,36,68),(145,275,1080,2010)); p.grid(); stats=plot_objects(p,layout)
    p.line([(0,60),(2.25,45.25),(-7,29.5),(-2,13),(-2,2.2),(-2,-4),(-12,-4.5),(-12,-13.5),(-12,-23),(-12,-28),(0.24,-31.63),(0.24,-53),(0,-60),(0,-72),(0,-76),(0,-86.5),(0,-97.5)])
    p.line([(-2,-4),(8,-4),(8,-13)],OPTION,5,True)
    p.line([(-12,-28),(-12,-37.5),(-12,-43)],OPTION,5,True)
    p.line([(0,-72),(0,-76.5)],OPTION,5,True)
    # Track direction comes from actual rail geometry, not constraint-dummy yaw.
    p.line([(3,-26),(3,-54)],MOTION,4,True)
    p.line([(3,-73),(3,-55)],MOTION,4,True)
    p.line([(-4,-78),(-2,-85.5)],MOTION,4,True)
    p.line([(4,-78),(2,-85.5)],MOTION,4,True)
    # Local +Y bridge rotates about +X into +Z, toward the entrance.
    p.line([(-4,2.2),(-4,10.2),(0,10.2),(0,2.2)],MOTION,3,True,False)
    badges=[((0,60),'S',0,0),((2.25,45.25),'01',55,0),((-7,29.5),'02',-55,0),((-2,2.2),'03',48,12),((-12,-4.5),'04a',-60,15),((-12,-13.5),'04b',-60,0),((-12,-23),'04c',-60,0),((-12,-37.5),'04d',-60,0),((0.24,-31.63),'05',55,0),((0,-71.8),'06',-52,0),((3,-73),'07',58,25),((4,-78),'08',60,-6),((0,-86.5),'09',60,0),((0,-97.5),'E',0,-10),((8,-13),'A',47,0),((-12,-43),'B',-50,-12),((0,-76.5),'C',-50,0)]
    for point,label,dx,dy in badges: p.badge(point,label,dx,dy)
    p.tag((9,56),'入口 Y=−16',0,-5)
    p.tag((7,39),'台面 −15.92',10,0)
    p.tag((-28,24),'台面 −18.31',-8,0)
    p.tag((-29,-52),'初始姿态投影',0,0)
    rows=[
      ('01','第一悬摆板','(2.25,−16.220,45.25)；台面 Y=−15.920','4×4 台面，四角悬绳；前进时利用摆动完成接岸。'),
      ('02','第二悬摆板','(−7,−18.612,29.5)；台面 Y=−18.312','第二组四角悬绳；位置偏左，原作只有 2 块摆板。'),
      ('03','可放下木吊桥','铰接枢轴 (−2,−15.7,2.2)','破坏两侧绳索后朝入口方向放倒连路；紫虚框为放平位置示意。'),
      ('04','四颗刺球与分支','X=−12；Z=−4.5 / −13.5 / −23 / −37.5','1 小+3 大。A 为右侧宝箱，B 在最后一颗刺球后方；主路转向 05。'),
      ('05','带坡度的滑轨平台','(0.2375,−15.3906,−31.6290)','真实轨道沿 Z，约 4° 坡度；原作通过磁力操作，不是水平自动往返。'),
      ('06','四层可拉动滑块','X=0；Z≈−72；初始台面 Y=−15 / −13 / −11 / −9','分别沿 Z 调整位置形成高差路线；C 宝箱在 (0,−17,−76.5) 下层。'),
      ('07','水晶开关','(3,−8,−73)','开关关联最终吊灯组；机关联动以原始对象链接为依据。'),
      ('08','双悬灯','(−4,4,−78) 及 (4,4,−78)','与绳索、门前可燃物构成联动；吊灯并非两座独立落脚平台。'),
      ('09','木阻挡与双扇金属门','门枢轴 (±3,4.6,−86.5)；木阻挡 (0,4.8,−86)','解除木构件后操作双门，进入祭坛；门的模型旋转已计入平面投影。'),
    ]
    mechanism_rows(d,rows,step=132)
    legend(d,1220,1540)
    map3_profile(d)
    small_note(d,1220,1780,[
      'Y 值为原始高度，负数不表示水下死亡区域。',
      '叠层滑块在俯视图重叠，右图展开说明层间关系。',
      '主线包含机关操作后的跨越，不是已验证的行走轨迹。',
      '保留原作机关语义；本项目能力适配需单独实现。',
    ])
    tx(d,(1220,2010),'原始坐标比例白盒 ≠ 像素级原模型',32,'#9d6049')
    footer(d,'来源：Dungeon058 / ZeldaMods radar；天花与装饰已剖去')
    path=OUT/'Map3_摩拉塔尼阿_原始坐标流程图.png'; im.save(path)
    print(path); print('Map3 projected modules:',stats)

def make_index():
    cards=[('Map2｜加·巴希夫','Map2_加巴希夫_原始坐标流程图.png','原始坐标 / 新重构'),('Map2.5｜卡奥·玛卡','Map2_5_卡奥玛卡_平面流程图.png','前次版本 / 参考图（场景后续修改未同步）'),('Map3｜摩·拉塔尼阿','Map3_摩拉塔尼阿_原始坐标流程图.png','原始坐标 / 新重构'),('Map3.5｜夏·塔瓦','Map3_5_夏塔瓦_平面流程图.png','前版白盒 / 飞行适配')]
    body=''.join(f'<article><header><h2>{html.escape(t)}</h2><span>{html.escape(note)}</span></header><a href="{html.escape(fn)}" target="_blank"><img src="{html.escape(fn)}" alt="{html.escape(t)}平面流程图"></a><p><a href="{html.escape(fn)}" download>下载原图</a> · 点击图片查看全尺寸</p></article>' for t,fn,note in cards)
    page='''<!doctype html><html lang="zh-CN"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>泡涌 · 四关平面流程图</title><style>body{margin:0;background:#edf1f0;color:#173e46;font-family:"Microsoft YaHei",sans-serif}main{max-width:1600px;margin:40px auto;padding:0 26px}h1{font-size:36px;margin-bottom:12px}.intro{line-height:1.8;color:#58757a}.grid{display:grid;grid-template-columns:1fr 1fr;gap:24px;margin-top:32px}article{border:1px solid #c9d5d5;border-radius:18px;background:white;overflow:hidden}header{padding:20px 24px;display:flex;align-items:center;justify-content:space-between;gap:12px}h2{font-size:21px;margin:0}span{font-size:13px;color:#58757a}img{width:100%;display:block}p{padding:0 24px;color:#58757a;font-size:14px}a{color:#057fb8}@media(max-width:850px){.grid{grid-template-columns:1fr}header{display:block}span{display:block;margin-top:8px}}</style><main><h1>《泡涌》四关平面流程图</h1><div class="intro">Map2 / Map3 按原作物件坐标与包围尺寸绘制；Map2.5 / Map3.5 保留前版白盒图。<br>机关编号、流程和高度见各图。原始坐标比例白盒不等于像素级原模型；流程未经通关验证。</div><section class="grid">'''+body+'</section></main></html>'
    path=OUT/'四关平面流程图索引.html'; path.write_text(page,encoding='utf-8'); print(path)

if __name__=='__main__':
    make_map2(); make_map3(); make_index()
