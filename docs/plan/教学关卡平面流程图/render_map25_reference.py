"""Draw Map2.5 from the source-coordinate Unity manifest; does not edit scenes."""
from pathlib import Path
import json
import math
from PIL import Image, ImageDraw
from render_reference_plans import (OUT, DATA, BG, INK, MUTED, LINE, MAIN, OPTION,
    MOTION, MECH, BREAK, Plan, corners, hull, transform, line, tx, roundbox)

W, H = 3400, 2400
FILE = 'Map2_5_卡奥玛卡_原始坐标流程图.png'


def draw_objects(p, layout):
    for floor in layout.get('supportGeometry', []):
        x, z = floor['position']['x'], floor['position']['z']
        hx, hz = floor['size']['x']/2, floor['size']['z']/2
        p.poly([(x-hx,z-hz),(x+hx,z-hz),(x+hx,z+hz),(x-hx,z+hz)], '#f4f6f4', '#d3ded9')
    visible = []
    for o in layout['objects']:
        n, g = o['name'], o['group']
        cc = corners(o)
        low = min(v[1] for v in cc)
        if g == 'Detail' and not any(s in n for s in ('Road', 'DoorIronRail')):
            continue
        if 'Ceiling' in n or 'TowerRoof' in n or low > 30:
            continue
        if 'BoxCover' in n:
            continue
        visible.append(o)
    visible.sort(key=lambda o: (o['group'] in ('Mechanisms', 'Breakable', 'Landmarks'), max(v[1] for v in corners(o))))
    for o in visible:
        n, g, k = o['name'], o['group'], o['kind']
        cc = corners(o)
        poly = hull([(v[0], v[2]) for v in cc])
        if not poly:
            continue
        if max(x for x,z in poly) < p.xmin or min(x for x,z in poly) > p.xmax or max(z for x,z in poly) < p.zmin or min(z for x,z in poly) > p.zmax:
            continue
        height = max(v[1] for v in cc) - min(v[1] for v in cc)
        fill, edge = '#d9e3e2', '#94adae'
        if g == 'Mechanisms': fill, edge = MECH, '#2b7778'
        elif g == 'Breakable': fill, edge = BREAK, '#a15f57'
        elif g == 'Landmarks': fill, edge = '#e6cc85', '#9b8136'
        elif g == 'Detail': fill, edge = '#edf0dd', '#b7c2af'
        # Tall partition meshes are outlines in this cutaway, never opaque floor.
        if k == 'frame' or (g in ('Structure', 'PartitionWalls') and height > 20):
            fill = None
        p.poly(poly, fill, edge)
        if k in ('stairs', 'slope'):
            a, b = o['boundsMin'], o['boundsMax']
            for t in range(1,7):
                z = a['z'] + (b['z']-a['z'])*t/7
                va = transform(o,(a['x'],(a['y']+b['y'])/2,z))
                vb = transform(o,(b['x'],(a['y']+b['y'])/2,z))
                p.line([(va[0],va[2]),(vb[0],vb[2])], '#829fa1', 2, False, False)
    return len(visible)


def row(d, y, no, title, coordinate, desc):
    roundbox(d,(1645,y-10,3320,y+136),fill='#f1f5f4',edge='#f1f5f4',r=12)
    d.ellipse((1662,y+8,1720,y+66),fill=INK)
    tx(d,(1691,y+37),no,25,'white','mm')
    tx(d,(1746,y),title,32)
    tx(d,(1746,y+45),coordinate,25,'#356f76')
    tx(d,(1746,y+84),desc,25,MUTED)


def render():
    layout = json.loads((DATA/'Map2_5_ReferenceLayout.json').read_text(encoding='utf-8-sig'))
    im = Image.new('RGB',(W,H),BG); d = ImageDraw.Draw(im)
    tx(d,(65,30),'MAP 2.5',42,'#3d8d8a')
    tx(d,(360,23),'卡奥·玛卡｜连接道路之物',57)
    tx(d,(68,107),'原始坐标重构图 · 双门进场 → 裂墙释放门片 → 重复搭接 → 高台祭坛',28,MUTED)
    roundbox(d,(55,182,1580,2265))
    roundbox(d,(1615,182,3345,2265))
    tx(d,(90,208),'剖顶平面  X–Z',32)
    tx(d,(1538,214),'−Z ↑',26,MUTED,'rt')
    tx(d,(1655,208),'机关位置 / 操作顺序 / 台面高度',34)

    # Preserve X:Z ratio and the existing sheets' 9.5 px per source unit.
    p = Plan(d,(-65,-48,43,96),(150,310,1515,2110))
    p.grid(); shown = draw_objects(p,layout)

    # Walkable ground path to the detachable door. Later lines describe a solved
    # door placement, not a claim that permanent bridges exist in the scene.
    p.line([(0,84.5),(0,60),(0,51),(-26,51),(-26,34),(-32,34)])
    p.line([(-30,29),(-25,26),(16,26),(16,38)])
    p.line([(16,38),(10,38),(10,34),(10,30),(10,21),(12,14),(12,6),(12,-3),(12,-9),(12,-16),(12,-23.5)])
    # Explicit operation spans; there are no permanent bridges at these points.
    p.line([(10,30),(10,21)],MOTION,6,True)
    p.line([(12,6),(12,-3)],MOTION,6,True)
    p.line([(-34,26),(-48.5,26.5)],OPTION,5,True)
    p.line([(8,14),(-4,14),(-12,14),(-12,10)],OPTION,5,True)
    p.line([(-12,14),(-14,7.5),(-14,15)],OPTION,4,True)

    marks=[((0,84.5),'S',0,0),((0,57),'01',60,0),((-34,34),'02',-65,-25),
           ((-34,29),'03',-65,25),((16,32),'04',65,20),((12,25.5),'05',70,0),
           ((-14,7.5),'06',-70,-10),((-12,10),'07',-25,-80),
           ((12,1.5),'08',75,0),((12,-18.5),'09',80,0),((12,-27),'E',0,-20),
           ((-48.5,26.5),'A',-30,0),((-14,15),'B',-75,18)]
    for point,no,dx,dy in marks: p.badge(point,no,dx,dy)
    # Height tags sit outside the flow centreline.
    for point,label,dx,dy in [
        ((8,72),'入口 / 地面 Y=0',18,0),
        ((-20,44),'左南岛 Y=4',-95,0),
        ((18,43),'起步岛 Y=4',20,0),
        ((17,12),'右岛 Y=4',25,0),
        ((-23,17),'左岛 Y=8',-115,-10),
        ((17,-6),'前阶台 Y=8',25,-15),
        ((15,-24),'祭坛区 Y=10',30,-5),
        ((-27,2),'秘密洞底 Y=2',-40,-8)]:
        p.tag(point,label,dx,dy,size=23)

    rows=[
        ('01','入口双扇金属门','模型原点 (−3,6.1,57) / (3,6.1,57)','入口在 +Z 端；推开双门后进入同一个开阔主厅。'),
        ('02','左侧裂墙 / 释放门片','裂块 X=−34，Z=34；底层 Y=0 / 4 / 8','3 块裂墙堆叠，破坏后解除侧门固定关系。'),
        ('03','可取下的门片','模型原点 (−34,6.1,29)；铰链约 (−33.65,6.1,31.7)','同一门片反复搬运、倾斜搭接；A 为侧室支路。'),
        ('04','右南起步岛与阶梯','阶梯 (16,0,32)；台面 Y=4','由地面登上右南岛；南部左右两岛中间留有缺口。'),
        ('05','第一次门片搭接','缺口约 X=10，Z=21.5…30；两岸台面 Y=4','将起步岛与右侧岛连接；紫虚线仅标操作区。'),
        ('06','藏在岛壁内的金属块','(−14,2,7.5)；4×4×4；洞底 Y=2','位于左岛低处；拉开后进入 B 秘密空腔。'),
        ('07','石块堆叠支路','石堆约 X=−12，Z=10；台面最高 Y=16','从左岛 Y=8 向上探索；上方是原作奖励点。'),
        ('08','第二次门片搭接 / 高差','右岛 Y=4 → 前阶台 Y=8；约 Z=5.5…−2.5','重新拿取门片并倾斜搭路，再进入终点坡道。'),
        ('09','坡道与祭坛区','坡道 (12,8,−12.5)；目标 (12,11.5,−23.5)','坡面由 Y=8 升到 Y=10；祭坛基座位于 (12,10,−27)。'),
    ]
    for i,r in enumerate(rows): row(d,312+i*159,*r)

    tx(d,(1665,1790),'读图方式',31)
    legend=[(MAIN,'蓝线：机关操作后的建议流程',False),
            (OPTION,'橙虚线：原作奖励 / 秘密区域支路',True),
            (MOTION,'紫虚线：可移动门片的搭接区',True)]
    for i,(color,label,dashed) in enumerate(legend):
        y=1850+i*58
        line(d,[(1667,y+15),(1740,y+15)],color,6,dashed)
        tx(d,(1762,y),label,26,MUTED)
    tx(d,(1665,2048),'蓝线不是已验证的通关轨迹；图中没有虚构永久桥。',27,'#9d6049')
    tx(d,(1665,2098),'门的搬运、固定解除及能力适配需按实际组件实现。',26,MUTED)
    tx(d,(1665,2148),'Y 为原作高度；模型原点与铰链不同，平台标台面。',25,MUTED)
    tx(d,(1665,2198),'浅底为房壳地面代理；剖去天花；敌人物件未纳入。',25,MUTED)

    x,y=p.p(-57,92)
    line(d,[(x,y),(x+20*p.s,y)],INK,3,False,False)
    tx(d,(x,y+20),'20 原作单位',22,MUTED)
    tx(d,(65,2310),'1 原作单位 = 1 Unity 单位 · 原始 SRT + 模型包围尺寸投影 · 数据来源：Dungeon054 / ZeldaMods radar',25,MUTED)
    tx(d,(65,2350),'这是原始坐标比例白盒；不是原始网格，也未声明像素级外观或原作机关逻辑完全复现。',23,MUTED)
    tx(d,(3320,2350),'2026.10.18',20,MUTED,'rt')
    path = OUT/FILE; im.save(path)
    print(path); print('Projected modules:',shown)


def update_index():
    path=OUT/'四关平面流程图索引.html'
    if path.exists():
        s=path.read_text(encoding='utf-8-sig')
        s=s.replace('Map2_5_卡奥玛卡_平面流程图.png',FILE)
        s=s.replace('前次版本 / 参考图（场景后续修改未同步）','原始坐标 / 新重构')
        s=s.replace('Map2 / Map3 按原作物件坐标与包围尺寸绘制；Map2.5 / Map3.5 保留前版白盒图。','Map2 / Map2.5 / Map3 按原作物件坐标与包围尺寸绘制；Map3.5 保留前版白盒图。')
        path.write_text(s,encoding='utf-8')
        print(path)


if __name__=='__main__':
    render()
    update_index()
