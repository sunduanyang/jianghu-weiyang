"""The eight regions of the jianghu (lon, lat): no dynasties, no borders to conquer.

Each region has one city, one local government (府衙) and one sect. The outline below
is a hand-generalized painted-map range of the lands the game covers; it is not a
historical frontier and not a union of modern provinces.
"""

ERA = {
    'year': 1200, 'title': '江湖八方', 'label': '江湖八方 · 八城八派',
    'note': '天下分为八个地区，各有一城、一座府衙、一个门派；不设王朝疆界与攻城。地名取通俗称呼，山川为示意。',
    'sources': [
        {'title': '宋史·地理志（卷八十五起）', 'url': 'https://zh.wikisource.org/wiki/宋史/卷085'},
        {'title': '泉州草庵摩尼光佛造像', 'url': 'https://zh.wikipedia.org/wiki/草庵'},
    ],
}

# Region records keep the field names the engine uses for jurisdictions ("countries").
COUNTRIES = [
    dict(id=0, name='中原', color='#b59a6d', capital='洛阳', kind='地区', label=[113.6, 35.4],
         government_name='洛阳府衙', ethos='天下之中，河洛交汇，驿道通向四方'),
    dict(id=1, name='关中', color='#b6817e', capital='长安', kind='地区', label=[107.6, 35.6],
         government_name='长安府衙', ethos='八百里秦川，东出潼关，西通河西'),
    dict(id=2, name='荆楚', color='#8d9b68', capital='襄阳', kind='地区', label=[112.3, 29.6],
         government_name='襄阳府衙', ethos='汉水与大江之间，湖泽纵横，南北要冲'),
    dict(id=3, name='巴蜀', color='#c08a5a', capital='成都', kind='地区', label=[105.0, 30.2],
         government_name='成都府衙', ethos='四塞之国，锦水穿城，蜀道险绝'),
    dict(id=4, name='江南', color='#6fa39a', capital='苏州', kind='地区', label=[118.8, 30.6],
         government_name='苏州府衙', ethos='水网密布，丝米之乡，商旅辐辏'),
    dict(id=5, name='滇黔', color='#a69ca9', capital='大理', kind='地区', label=[103.0, 25.4],
         government_name='大理府衙', ethos='苍山洱海，苗寨山林，瘴气与蛊毒传说并存'),
    dict(id=6, name='闽浙', color='#769fa0', capital='泉州', kind='地区', label=[117.6, 26.3],
         government_name='泉州州衙', ethos='刺桐海港，番舶云集，诸教并存'),
    dict(id=7, name='西北', color='#7d97b3', capital='凉州', kind='地区', label=[101.0, 38.4],
         government_name='凉州州衙', ethos='河西走廊，驼铃与佛塔相望，出塞即是大漠'),
]

# One range for the whole jianghu; every region's cell is clipped to it.
JIANGHU_RANGE = [
    (94.2, 40.0), (95.6, 39.6), (97.2, 39.1), (99.2, 38.3), (101.0, 37.3), (100.6, 36.3),
    (101.7, 35.1), (102.9, 34.4), (103.4, 33.0), (102.5, 32.4), (102.3, 30.5), (101.6, 28.6),
    (99.5, 28.4), (98.7, 27.5), (98.3, 25.4), (97.6, 24.7), (98.7, 23.6), (99.6, 22.1),
    (101.2, 21.3), (102.2, 22.4), (103.9, 22.6), (105.5, 23.2), (106.6, 22.4), (108.0, 21.5),
    (109.6, 21.0), (110.0, 20.35), (110.8, 20.35), (111.5, 20.9), (114.0, 21.4), (117.0, 22.3),
    (118.6, 23.3), (120.0, 25.0), (120.9, 26.8), (123.0, 29.0), (123.2, 31.5), (122.5, 34.5),
    (123.4, 37.4), (121.8, 38.2), (120.2, 39.2), (119.6, 40.2), (117.6, 40.9), (115.0, 41.2),
    (113.3, 41.0), (111.4, 40.5), (109.7, 39.8), (107.6, 39.2), (105.6, 38.9), (104.0, 38.6),
    (102.8, 38.9), (100.8, 39.6), (98.6, 40.3), (96.5, 40.8), (94.4, 40.8),
]
TERRITORIES = [{'country': c['id'], 'rings': [JIANGHU_RANGE]} for c in COUNTRIES]

# Sub-region names written on the map near each city.
REGIONS = [
    (0, '河洛', 112.9, 34.1), (1, '三秦', 108.4, 34.0), (2, '汉水', 112.6, 31.6),
    (3, '川西', 103.6, 31.2), (4, '吴中', 120.4, 31.8), (5, '苍洱', 100.6, 26.6),
    (6, '刺桐', 118.4, 25.6), (7, '河西', 102.0, 37.2),
]

RIVERS = [
    ('长江', [[96,32],[99,27.5],[102,26.5],[104,28.5],[106.5,29.5],[110,30.8],
              [112,30.5],[114.3,30.5],[116,29.7],[118.5,31.5],[121.5,31.5]]),
    ('黄河', [[96,35],[99,35.5],[101,36],[104,36],[106,37],[106.5,39],[108,40.5],
              [111,40.8],[111,37],[110.5,35],[113,34.8],[114.3,34.8],[117.2,34.3],[119,33.6],[120.3,33.7]]),
    ('淮水', [[113.9,32.5],[114.7,32.3],[115.6,32.45],[116.7,32.6],[117.4,32.9],
              [118.5,33.2],[119.4,33.4],[120.3,33.7]]),
]
