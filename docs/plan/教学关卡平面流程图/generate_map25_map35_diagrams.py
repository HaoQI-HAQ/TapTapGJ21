from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math


OUT = Path(__file__).resolve().parent
FONT_PATH = r"C:\Windows\Fonts\msyh.ttc"

BG = "#F4F8F7"
INK = "#16343A"
MUTED = "#557176"
GRID = "#DCE7E5"
WALL = "#536D70"
FLOOR = "#E2ECE9"
PLATFORM = "#B6D5D2"
PLATFORM_HI = "#85C2BC"
MECH = "#2A9D9A"
ROUTE = "#137A9A"
OPTIONAL = "#D9822B"
HAZARD = "#CF594D"
GOAL = "#E2B94D"
VOID = "#D9E7E8"


def font(size):
    return ImageFont.truetype(FONT_PATH, size=size)


def rounded(draw, xy, fill, outline=None, radius=18, width=2):
    draw.rounded_rectangle(xy, radius=radius, fill=fill, outline=outline, width=width)


def text(draw, xy, value, size=26, fill=INK, anchor=None, stroke=0):
    draw.text(xy, value, font=font(size), fill=fill, anchor=anchor,
              stroke_width=stroke, stroke_fill=BG if stroke else None)


def map_transform(x0, y0, scale, xmin, zmax):
    def conv(x, z):
        return (x0 + (x - xmin) * scale, y0 + (zmax - z) * scale)
    return conv


def world_box(draw, conv, x, z, w, d, fill, outline=None, width=2, radius=0):
    left, top = conv(x - w / 2, z + d / 2)
    right, bottom = conv(x + w / 2, z - d / 2)
    box = (left, top, right, bottom)
    if radius:
        draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)
    else:
        draw.rectangle(box, fill=fill, outline=outline, width=width)
    return box


def world_line(draw, conv, points, fill, width=6, dash=False, arrow=True):
    pts = [conv(x, z) for x, z in points]
    for a, b in zip(pts, pts[1:]):
        if not dash:
            draw.line((a, b), fill=fill, width=width)
        else:
            dx, dy = b[0] - a[0], b[1] - a[1]
            dist = math.hypot(dx, dy)
            if dist == 0:
                continue
            ux, uy = dx / dist, dy / dist
            for start in range(0, int(dist), 25):
                end = min(start + 13, dist)
                draw.line((a[0] + ux * start, a[1] + uy * start,
                           a[0] + ux * end, a[1] + uy * end), fill=fill, width=width)
    if arrow and len(pts) > 1:
        a, b = pts[-2], pts[-1]
        angle = math.atan2(b[1] - a[1], b[0] - a[0])
        tip = b
        length, spread = 20, 0.62
        p1 = (tip[0] - length * math.cos(angle - spread), tip[1] - length * math.sin(angle - spread))
        p2 = (tip[0] - length * math.cos(angle + spread), tip[1] - length * math.sin(angle + spread))
        draw.polygon((tip, p1, p2), fill=fill)


def world_rotated_box(draw, conv, cx, cz, length, depth, angle_deg, fill, outline, width=3, dashed=False):
    a = math.radians(angle_deg)
    ux, uz = math.cos(a), -math.sin(a)
    vx, vz = math.sin(a), math.cos(a)
    corners = []
    for along, across in [(-length/2, -depth/2), (length/2, -depth/2),
                          (length/2, depth/2), (-length/2, depth/2)]:
        x = cx + ux * along + vx * across
        z = cz + uz * along + vz * across
        corners.append(conv(x, z))
    if not dashed:
        draw.polygon(corners, fill=fill, outline=outline, width=width)
    else:
        for a, b in zip(corners, corners[1:] + corners[:1]):
            dx, dy = b[0] - a[0], b[1] - a[1]
            dist = math.hypot(dx, dy)
            if dist == 0:
                continue
            ux, uy = dx / dist, dy / dist
            for start in range(0, int(dist), 18):
                end = min(start + 9, dist)
                draw.line((a[0] + ux * start, a[1] + uy * start,
                           a[0] + ux * end, a[1] + uy * end), fill=outline, width=width)
    return corners


def number_marker(draw, conv, x, z, label, color=MECH, radius=25):
    px, py = conv(x, z)
    draw.ellipse((px-radius, py-radius, px+radius, py+radius), fill=color, outline=BG, width=4)
    text(draw, (px, py-1), label, 25, fill="#FFFFFF", anchor="mm")


def world_tag(draw, conv, x, z, label, size=19):
    px, py = conv(x, z)
    f = font(size)
    bbox = draw.textbbox((0, 0), label, font=f)
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
    rect = (px - w / 2 - 8, py - h / 2 - 5, px + w / 2 + 8, py + h / 2 + 5)
    draw.rounded_rectangle(rect, radius=7, fill="#F8FBFA", outline="#C8D9D6", width=1)
    draw.text((px, py), label, font=f, fill=INK, anchor="mm")


def add_grid(draw, conv, xmin, xmax, zmin, zmax, step=5):
    for x in range(math.ceil(xmin / step) * step, xmax + 1, step):
        x1, y1 = conv(x, zmax)
        x2, y2 = conv(x, zmin)
        draw.line((x1, y1, x2, y2), fill=GRID, width=1)
        text(draw, (x1, y2 + 10), f"{x}", 17, fill=MUTED, anchor="mt")
    for z in range(math.ceil(zmin / step) * step, zmax + 1, step):
        x1, y1 = conv(xmin, z)
        x2, y2 = conv(xmax, z)
        draw.line((x1, y1, x2, y2), fill=GRID, width=1)
        text(draw, (x1 - 13, y1), f"{z}", 17, fill=MUTED, anchor="rm")


def right_panel(draw, top, title, rows, width=780):
    x0, x1 = 1390, 2390
    rounded(draw, (x0, top, x1, 1680), "#FFFFFF", outline="#C8D9D6", radius=24, width=3)
    text(draw, (x0 + 36, top + 34), title, 34, fill=INK)
    y = top + 98
    for kind, heading, body in rows:
        color = {"route": ROUTE, "optional": OPTIONAL, "mechanism": MECH,
                 "hazard": HAZARD, "goal": GOAL, "note": MUTED}.get(kind, MECH)
        rounded(draw, (x0 + 34, y, x0 + 80, y + 46), color, radius=22, width=1)
        text(draw, (x0 + 57, y + 22), heading.split(" ")[0], 21, fill="#FFFFFF", anchor="mm")
        text(draw, (x0 + 98, y + 2), heading, 25, fill=INK)
        for line in body.split("\n"):
            text(draw, (x0 + 100, y + 40), line, 21, fill=MUTED)
            y += 31
        y += 43


def make_map25():
    image = Image.new("RGB", (2500, 1800), BG)
    draw = ImageDraw.Draw(image)
    text(draw, (90, 48), "MAP 2.5  ·  卡奥·玛卡", 48, fill=INK)
    text(draw, (90, 108), "连接道路之物｜当前白盒平面 + 预期流程  ·  北向 +Z  ·  网格每格 5 m", 25, fill=MUTED)
    xmin, xmax, zmin, zmax = -39, 28, -30, 45
    conv = map_transform(140, 168, 16.5, xmin, zmax)
    add_grid(draw, conv, xmin, xmax, zmin, zmax, 5)

    # Main chamber, entrance run-in, and west mechanism room.
    world_box(draw, conv, 0, 12, 46, 54, FLOOR, WALL, 5)
    world_box(draw, conv, 0, -22, 15, 14, "#DCEAE7", WALL, 4)
    world_box(draw, conv, -30, 1, 13, 20, "#E8EEE9", WALL, 4)
    # Openings/gates are marked over the room boundary.
    world_line(draw, conv, [(-7, -15), (-2.7, -15)], WALL, 9, arrow=False)
    world_line(draw, conv, [(2.7, -15), (7, -15)], WALL, 9, arrow=False)
    world_line(draw, conv, [(-23, -4), (-23, -1.7)], WALL, 8, arrow=False)
    world_line(draw, conv, [(-23, 4.2), (-23, 7.5)], WALL, 8, arrow=False)
    # Gate leaves shown as the mechanism locations.
    world_box(draw, conv, -2.35, -15, 4.5, .55, MECH, MECH)
    world_box(draw, conv, 2.35, -15, 4.5, .55, MECH, MECH)
    world_box(draw, conv, -23, -1.8, .55, 5.8, MECH, MECH)
    world_box(draw, conv, -23, 4.4, .55, 5.8, MECH, MECH)

    # Main platforms and step run.
    world_box(draw, conv, 10, 7, 11, 10, PLATFORM, MECH, 3, radius=8)
    for i in range(6):
        z = 1.3 + i * 1.05
        world_box(draw, conv, 13.5, z, 5.2, 1.2, "#C5DBD7", WALL, 2)
    world_box(draw, conv, 1, 17, 11, 10, PLATFORM_HI, MECH, 3, radius=8)
    world_box(draw, conv, 1, 21.2, 7, 3.2, "#A8CBC5", WALL, 2)
    world_box(draw, conv, 12, 25, 12, 12, PLATFORM_HI, MECH, 3, radius=8)
    world_box(draw, conv, 13, 25, 5, 5, "#97BDB8", WALL, 2, radius=4)
    world_box(draw, conv, 18.2, 26, 0.8, 4, WALL, WALL, 2)
    world_box(draw, conv, 17.5, 23.1, 1.3, 2.4, MECH, "#146D70", 3, radius=4)
    world_box(draw, conv, 19.4, 23.1, 4, 4.5, "#C8E3DF", MECH, 2, radius=6)
    world_box(draw, conv, 3, 37, 14, 11, "#F2E6BD", GOAL, 4, radius=10)

    # The released door is one object. A/B/C are successive placement guides.
    world_box(draw, conv, -20.9, 0.8, 1.0, 8.2, "#A9DCDA", MECH, 3, radius=3)
    world_rotated_box(draw, conv, 5.7, 12, 12, 3.4, -40, "#D5EEEB", MECH, 3, dashed=True)
    world_rotated_box(draw, conv, 6.5, 21, 12, 3.4, -35, "#D5EEEB", MECH, 3, dashed=True)
    world_rotated_box(draw, conv, 7.5, 31, 14, 3.4, 38, "#D5EEEB", MECH, 3, dashed=True)

    # Main route and optional hidden-space branch.
    world_line(draw, conv, [(0, -25), (0, -12), (-13, -3), (-18, 1), (-22.1, 5.9)], ROUTE, 8)
    world_line(draw, conv, [(-22.1, 5.9), (-20.9, .8), (13.5, 1)], ROUTE, 8)
    world_line(draw, conv, [(13.5, 1), (10, 7), (1, 17)], ROUTE, 8)
    world_line(draw, conv, [(1, 17), (9, 23), (9, 28), (3, 37)], ROUTE, 8)
    world_line(draw, conv, [(9, 28), (17, 28), (17.5, 23.1), (19.4, 23.1)], OPTIONAL, 6, dash=True)
    world_tag(draw, conv, 10.5, 4, "低岛 Y=0.50")
    world_tag(draw, conv, -.2, 14.5, "中岛 Y=2.50")
    world_tag(draw, conv, 12, 20.3, "石柱岛 Y=4.60")
    world_tag(draw, conv, 3, 41, "祭坛台 Y=7.60")

    # Numbered mechanism markers.
    number_marker(draw, conv, -22.1, 5.9, "1", HAZARD)
    number_marker(draw, conv, -20.9, .8, "2", MECH)
    number_marker(draw, conv, 13.5, 1.0, "3", MECH)
    number_marker(draw, conv, 19.4, 23.1, "4", OPTIONAL)
    number_marker(draw, conv, 3, 37, "G", GOAL)
    for label, x, z in [("A", 5.7, 12), ("B", 6.5, 21), ("C", 7.5, 31)]:
        px, py = conv(x, z)
        text(draw, (px, py), label, 20, fill=INK, anchor="mm", stroke=2)

    # Start and north arrow.
    sx, sy = conv(0, -25)
    draw.ellipse((sx-24, sy-24, sx+24, sy+24), fill=ROUTE, outline="#FFFFFF", width=4)
    text(draw, (sx, sy), "S", 24, fill="#FFFFFF", anchor="mm")
    text(draw, (1090, 170), "N", 24, fill=INK, anchor="mm")
    draw.line((1090, 212, 1090, 180), fill=INK, width=5)
    draw.polygon([(1090, 168), (1079, 188), (1101, 188)], fill=INK)

    rows = [
        ("route", "S → 1 → 2 → 3 → A/B/C → G", "预期：进入主厅 → 西室解锁门板 → 返回低岛 → 逐级搭桥。\n蓝线表示流程意图，当前机关尚未完成联动。"),
        ("hazard", "1  炸弹门闩 / 西侧室", "预期炸断支撑，释放门板。当前为模型占位。"),
        ("mechanism", "2  可搬门板 / 西侧门后", "预期复用门板连接各岛；搬运功能尚未接入。\nA/B/C 为现有无碰撞参考姿态，尺寸和朝向仍需调整。"),
        ("mechanism", "3  低岛阶梯 / 主厅东南", "从连续底层地面接入抬高的岛台。台面 Y=0.50 m。"),
        ("optional", "4  暗格 / 石柱岛东侧", "预期移开侧面方块进入凹室；交互尚未接入。\n暗格底面 Y=2.35 m，低于石柱岛顶面。"),
        ("goal", "G  祭坛平台 / 北侧高台", "台面 Y=7.60 m；路线绕开岛上的实体石堆。"),
        ("hazard", "!  当前几何待修", "入口门缝只有 0.2 m；北墙穿过祭坛台。\n红圈为冲突点；门桥参考姿态不能视为已可通行。"),
        ("note", "图例", "蓝箭头=预期主线；橙虚线=预期支线；青虚线=桥位参考。\n浅灰底=连续地面；Y=可站立台面高度，单位 m。"),
    ]
    right_panel(draw, 165, "流程与机关位置", rows)
    for x, z, label in [(0, -15, "入口门缝"), (3, 39.4, "北墙穿台")]:
        px, py = conv(x, z)
        draw.ellipse((px-42, py-22, px+42, py+22), outline=HAZARD, width=4)
        text(draw, (px+52, py), "! " + label, 20, fill=HAZARD, anchor="lm", stroke=2)
    text(draw, (90, 1717), "依据当前白盒构建坐标绘制；不是原作实测复原图，流程未作通关验证。完整机关状态及坐标见同目录说明。", 20, fill=MUTED)
    image.save(OUT / "Map2_5_卡奥玛卡_平面流程图.png", optimize=True)


def make_map35():
    image = Image.new("RGB", (2500, 1800), BG)
    draw = ImageDraw.Draw(image)
    text(draw, (90, 48), "MAP 3.5  ·  夏·塔瓦", 48, fill=INK)
    text(draw, (90, 108), "飞行路线箱庭｜当前白盒平面 + 高度投影  ·  北向 +Z  ·  高度均为台面 Y（米）", 25, fill=MUTED)

    # Plan view at left, drawn from current Unity X/Z coordinates.
    xmin, xmax, zmin, zmax = -20, 21, -21, 39
    conv = map_transform(125, 180, 17.5, xmin, zmax)
    add_grid(draw, conv, xmin, xmax, zmin, zmax, 5)
    # Vertical shaft boundary and open bottom.
    world_box(draw, conv, 0, 8, 30, 54, "#E7F0EF", None, 0)
    # Hide the underlay in the shaft and show a small title inside.
    world_box(draw, conv, 0, 8, 28.8, 52.5, VOID, None, 0)
    for wall_x, wall_z, wall_w, wall_d in [(-15, 8, .8, 54), (15, 8, .8, 54), (0, 35, 30, .8)]:
        world_box(draw, conv, wall_x, wall_z, wall_w, wall_d, WALL, WALL, 1)
    # Platform footprints.
    world_box(draw, conv, 0, -12, 14, 12, "#C5D8D7", WALL, 3, radius=7)
    world_box(draw, conv, 0, 1, 10, 8, PLATFORM, MECH, 3, radius=8)
    world_box(draw, conv, 8.5, 8, 8, 7, "#D6E8E5", MECH, 3, radius=8)
    world_box(draw, conv, 3.5, 13, 5, 5, "#D6E8E5", WALL, 3, radius=5)
    world_box(draw, conv, 14.5, 13, 5, 5, PLATFORM_HI, MECH, 3, radius=5)
    world_box(draw, conv, 11.5, 20, 15, 12, PLATFORM, MECH, 3, radius=8)
    # Moving platform path at Y=14.
    world_line(draw, conv, [(7, 13), (15, 13)], MECH, 7, dash=True)
    world_box(draw, conv, 7, 13, 4.6, 4.6, "#BCE5E0", MECH, 3, radius=5)
    # A vertical ladder projects to a single X/Z location.
    world_line(draw, conv, [(13.7, 16.2), (15.3, 16.2)], WALL, 8, arrow=False)
    # Pillar, its covered side opening, and upper landings.
    world_box(draw, conv, 3.5, 18.5, 2.2, 14, "#7D9899", WALL, 3)
    world_box(draw, conv, 6.5, 18.5, 8, 14, "#657E80", WALL, 3)
    # Frame only: the solid core behind it has not been cut open.
    world_box(draw, conv, 2.15, 18.25, 0.8, 3.5, "#F6D58C", HAZARD, 3)
    world_box(draw, conv, -4, 20, 8, 7, "#F5E8C9", OPTIONAL, 3, radius=8)
    world_box(draw, conv, 2, 25, 5, 4, "#D9E9E6", MECH, 3, radius=6)
    world_box(draw, conv, 7, 25, 6, 4, "#C8DDDA", MECH, 3, radius=6)
    world_box(draw, conv, 12.5, 26, 9, 7, "#B8D9D5", MECH, 3, radius=6)
    world_box(draw, conv, 7.5, 31, 13, 9, "#F2E6BD", GOAL, 4, radius=10)

    # Number markers and route lines.
    world_line(draw, conv, [(0, -12), (0, 1), (5, 8), (3.5, 13)], ROUTE, 7)
    world_line(draw, conv, [(3.5, 13), (14.5, 13), (14.5, 16.2)], ROUTE, 7)
    world_line(draw, conv, [(14.5, 16.2), (11.5, 20), (14, 24), (12.5, 26), (7.5, 31)], ROUTE, 7)
    # The optional sequence is disconnected at the uncut opening.
    world_line(draw, conv, [(-4, 20), (-4, 27), (2, 27), (7, 27), (12.5, 26)], OPTIONAL, 5, dash=True)
    world_tag(draw, conv, -1, -16, "入口 Y=0.00")
    world_tag(draw, conv, -1.8, 3.8, "Y=6.90")
    world_tag(draw, conv, 10, 7, "Y=13.90")
    world_tag(draw, conv, -4, 22.7, "Y=33.40")
    world_tag(draw, conv, 8.5, 34.2, "Y=37.40")
    world_tag(draw, conv, -6, 14, "侧孔未贯通", 20)
    world_line(draw, conv, [(-3, 14.8), (2.15, 18.25)], HAZARD, 3)
    number_marker(draw, conv, 0, -12, "S", ROUTE)
    number_marker(draw, conv, 0, 1, "1", MECH)
    number_marker(draw, conv, 7, 13, "2", MECH)
    number_marker(draw, conv, 14.5, 16.2, "3", MECH)
    number_marker(draw, conv, 2.15, 18.25, "4", HAZARD)
    number_marker(draw, conv, 12.5, 26, "5", MECH)
    number_marker(draw, conv, 7.5, 31, "G", GOAL)
    text(draw, (840, 175), "N ↑", 25, fill=INK)
    world_line(draw, conv, [(15, 11), (15, 28)], HAZARD, 5, dash=True, arrow=False)
    world_tag(draw, conv, 14.5, 30, "! 东墙冲突", 19)

    # Z/Y projection (not a physical cut): objects at different X overlap.
    prof_x0, prof_y0, prof_scale = 1080, 1200, 20.5
    z_min, z_max, y_min, y_max = -18, 39, -2, 40
    def prof(z, y):
        return (prof_x0 + (z - z_min) * prof_scale,
                prof_y0 - (y - y_min) * prof_scale)
    for z in range(-15, 40, 5):
        a, b = prof(z, y_min), prof(z, y_max)
        draw.line((a[0], a[1], b[0], b[1]), fill=GRID, width=1)
        text(draw, (a[0], prof_y0 + 22), f"Z{z}", 17, fill=MUTED, anchor="mt")
    for y in range(0, 41, 5):
        a, b = prof(z_min, y), prof(z_max, y)
        draw.line((a[0], a[1], b[0], b[1]), fill=GRID, width=1)
        text(draw, (prof_x0 - 16, a[1]), f"Y{y}", 17, fill=MUTED, anchor="rm")
    # Pillar projection. No opening is cut in this geometry.
    p1, p2 = prof(11.5, 19.5), prof(25.5, 29.5)
    draw.rectangle((p1[0], p2[1], p2[0], p1[1]), fill="#DCE4E2", outline="#92A6A4", width=2)
    text(draw, (p1[0]+14, p2[1]+14), "柱体投影（实心）", 20, fill=MUTED)
    # Section platforms (z center, y height, width along z, label).
    sections = [
        (-12, 0, 12, "入口"), (1, 6.9, 8, "1 首台"), (8, 13.9, 7, "早期落脚台"),
        (13, 14.7, 4.6, "2 移动台"), (20, 20.6, 12, "主层"),
        (25, 25.9, 4, "回返B"), (26, 28.9, 7, "5 接应台"),
        (25, 30.7, 4, "回返A"), (20, 33.4, 7, "远端支线"),
        (31, 37.4, 9, "G 祭坛"),
    ]
    for z, y, length, label in sections:
        p1 = prof(z - length/2, y)
        p2 = prof(z + length/2, y)
        color = GOAL if "祭坛" in label else OPTIONAL if "支线" in label else MECH if "移动台" in label else PLATFORM_HI
        draw.line((p1[0], p1[1], p2[0], p2[1]), fill=color, width=11)
        offset = -13 if label != "5 接应台" else 12
        anch = "mb" if offset < 0 else "mt"
        text(draw, ((p1[0]+p2[0])/2, p1[1]+offset), f"{label}  {y:.2f}", 18, fill=INK, anchor=anch, stroke=2)
    # Ladder is geometry only, drawn at its exact Z and Y extent.
    la, lb = prof(16.2, 14.52), prof(16.2, 19.30)
    draw.line((la, lb), fill=WALL, width=5)
    text(draw, (la[0]+12, (la[1]+lb[1])/2), "3 梯架", 18, fill=WALL)
    # The opening frame is highlighted, never drawn as a cut-through hole.
    af = prof(18.25, 25.5)
    draw.ellipse((af[0]-22, af[1]-22, af[0]+22, af[1]+22), fill=HAZARD, outline=BG, width=3)
    text(draw, af, "4", 22, fill="#FFFFFF", anchor="mm")
    # Main and optional section-route lines.
    seq_main = [(-12, 0), (1, 6.9), (8, 13.9), (13, 14.7), (20, 20.6), (26, 28.9), (31, 37.4)]
    pts = [prof(z, y) for z, y in seq_main]
    draw.line(pts, fill=ROUTE, width=6, joint="curve")
    for p in pts[1:-1]:
        draw.ellipse((p[0]-6, p[1]-6, p[0]+6, p[1]+6), fill=ROUTE)
    seq_opt = [(20, 33.4), (25, 30.7), (25, 25.9), (26, 28.9)]
    for a, b in zip(seq_opt, seq_opt[1:]):
        pa, pb = prof(*a), prof(*b)
        dx, dy = pb[0]-pa[0], pb[1]-pa[1]
        dist = math.hypot(dx, dy)
        ux, uy = dx/dist, dy/dist
        for start in range(0, int(dist), 22):
            end = min(start + 11, dist)
            draw.line((pa[0]+ux*start, pa[1]+uy*start, pa[0]+ux*end, pa[1]+uy*end), fill=OPTIONAL, width=4)

    # Right-side annotation panel.
    rounded(draw, (1010, 145, 2390, 275), "#FFFFFF", outline="#C8D9D6", radius=22, width=3)
    text(draw, (1050, 169), "左：X/Z 平面定位    右：Z/Y 高度投影", 27, fill=INK)
    text(draw, (1050, 218), "不同 X 的物件会在右图重叠；路线仅表示顺序，不表示穿过柱体。", 22, fill=MUTED)
    rounded(draw, (1010, 1290, 2390, 1690), "#FFFFFF", outline="#C8D9D6", radius=22, width=3)
    text(draw, (1050, 1317), "机关与预期路线", 30, fill=INK)
    rows = [
        ("route", "S → 1 → 2 → 3 → 5 → G", "入口 → 首台 → 横移平台 → 梯架/主层 → 绕柱接应台 → 祭坛。"),
        ("mechanism", "2  横向往返平台（已接脚本）", "中心 X=7→15→7，Z=13；台面 Y=14.70，往返约 13.33 秒。"),
        ("hazard", "4  侧孔造型（未贯通）", "背后仍是实心柱体；支线入口中断，远端及回返台仅保留位置关系。"),
        ("hazard", "!  东墙碰撞 / 梯架状态", "平台右端及多个落脚台与东墙重叠；3 只有梯架模型，未接攀爬。"),
        ("note", "5秒飞行限制", "蓝线为预期顺序；分段时长还需结合实际飞行速度校准，未验证可达。"),
    ]
    y = 1370
    for kind, heading, body in rows:
        color = {"route": ROUTE, "optional": OPTIONAL, "mechanism": MECH,
                 "hazard": HAZARD, "goal": GOAL, "note": MUTED}.get(kind, MECH)
        rounded(draw, (1050, y, 1096, y+44), color, radius=22, width=1)
        text(draw, (1073, y+22), heading.split(" ")[0], 19, fill="#FFFFFF", anchor="mm")
        text(draw, (1112, y+1), heading, 23, fill=INK)
        text(draw, (1112, y+32), body.split("\n")[0], 19, fill=MUTED)
        if "\n" in body:
            text(draw, (1112, y+58), body.split("\n")[1], 19, fill=MUTED)
            y += 31
        y += 61
    rounded(draw, (90, 1290, 950, 1690), "#FFFFFF", outline="#C8D9D6", radius=22, width=3)
    text(draw, (125, 1317), "台面高度与读图", 30)
    height_rows = [
        "出发台 / 到达台     Y=14.40 m",
        "主层                         Y=20.60 m",
        "回返 B / A                Y=25.90 / 30.70 m",
        "接应台 / 祭坛           Y=28.90 / 37.40 m",
    ]
    for i, row in enumerate(height_rows):
        text(draw, (125, 1375+i*44), row, 23, fill=MUTED)
    text(draw, (125, 1570), "蓝箭头=预期主线；橙虚线=高层回返顺序。", 21, fill=MUTED)
    text(draw, (125, 1610), "青虚线=移动轨迹；红色=未贯通/碰撞冲突。", 21, fill=MUTED)
    text(draw, (125, 1650), "图中不显示风柱和宝箱。", 21, fill=MUTED)
    text(draw, (90, 1730), "依据当前白盒构建坐标绘制；不是原作实测复原图，流程未作通关验证。本次仅整理图纸，未修改 Unity 场景。", 20, fill=MUTED)
    image.save(OUT / "Map3_5_夏塔瓦_平面流程图.png", optimize=True)


if __name__ == "__main__":
    make_map25()
    make_map35()
    print("Generated Map2.5 and Map3.5 floorplan diagrams.")
