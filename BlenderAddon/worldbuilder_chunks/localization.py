"""Small add-on-local UI localization service plus Blender native translations."""

MESSAGES={
    "en":{
        "language":"Language","auto":"Auto","korean":"Korean","english":"English",
        "chunk_terrain":"Chunk Terrain","terrain_fill":"Generate Selected Chunks","terrain_scope":"Scope",
        "active_chunk":"Active Chunk","selected_chunks":"Selected Chunks","rectangle":"Rectangle",
        "cells":"Cells per Chunk","base_height":"Base Height","relief":"Relief","feature_size":"Feature Size",
        "replace":"Replace Existing","sculpt":"Terrain Sculpt","begin_sculpt":"Begin Sculpt Session",
        "apply_sculpt":"Apply Sculpt","cancel_sculpt":"Cancel Sculpt","neighbor_ring":"Neighbor Ring",
        "lock_boundary":"Lock Outer Boundary","sculpt_hint":"Use fixed-topology Sculpt brushes. Dyntopo and remesh are rejected.",
        "asset_library":"Structure Library","registry":"Asset Registry","source_collection":"Source Collection",
        "asset_id":"Asset ID","name_ko":"Korean Name","name_en":"English Name","category":"Category",
        "ownership":"Streaming Ownership","save_asset":"Register / Update Asset","reload":"Reload Registry",
        "place_cursor":"Place at Cursor","place_surface":"Surface Placement Tool","search":"Search",
        "align_surface":"Align to Surface","scale_min":"Minimum Scale","scale_max":"Maximum Scale",
        "no_asset":"No registered asset is selected.","chunk":"Chunk","region":"Region",
        "vertical_layers":"Vertical Layers","layer_height":"Layer Height","layer_base":"Layer Base Z","layer_count":"Layer Count",
        "active_layer_index":"Active Layer","layer_from_object":"From Active Object","layer_frame":"Frame Layer",
        "layer_isolate":"Isolate","layer_apply_isolate":"Apply Isolation","layer_show_all":"Show All",
        "layer_selection":"Selection","layer_snap":"Snap to Floor","layer_follow_grid":"Grid Follows Layer",
        "layer_bands":"Layer Bands","layer_lock":"Lock Placement to Layer",
        "sea_level":"Sea Level","band_shallow":"Shallow","band_mid":"Mid","band_deep":"Deep","show_bands":"Show Depth Bands","cursor_depth":"Cursor Depth",
        "goto_chunk":"Go To Chunk","bookmark_add":"Save View","bookmark_jump":"Jump","bookmark_update":"Update","bookmark_name":"Name",
        "traversal_profile":"Profile","player_height":"Height","player_radius":"Radius","max_slope":"Max Slope","probe_spacing":"Spacing","traversal_scope":"Scope",
        "entity_catalog_file":"Catalog JSON","entity_catalog":"Entity Catalog","placement_kind":"Placement Kind","entity_prefab_id":"Entity Prefab ID","entity_kind":"Entity Kind",
        "entity_persistent":"Persistent","entity_region_streamed":"Region Streamed","entity_replicated":"Replicated",
        "world_grid":"World Grid","active_chunk_panel":"Active Chunk","chunk_object":"Chunk Object","unity_export":"Unity Export","biomes":"Biomes","scatter":"Scatter","splines":"Splines","seams":"Seams","bake":"Bake","stamps":"Stamps","analysis":"Analysis","vertex_bake":"Vertex Attribute Bake","terrain_toolkit":"Nex Stylized Terrain","rock_generator":"Stylized Rock Generator","validate_world":"Validate World","export_dirty":"Export Dirty Chunks","generate":"Generate","apply":"Apply","cancel":"Cancel",
    },
    "ko":{
        "language":"언어","auto":"자동","korean":"한국어","english":"영어",
        "chunk_terrain":"청크 지형","terrain_fill":"선택 청크 지형 생성","terrain_scope":"생성 범위",
        "active_chunk":"활성 청크","selected_chunks":"선택한 청크","rectangle":"사각형 범위",
        "cells":"청크당 셀 수","base_height":"기본 높이","relief":"높낮이","feature_size":"지형 특징 크기",
        "replace":"기존 지형 교체","sculpt":"지형 조형","begin_sculpt":"조형 세션 시작",
        "apply_sculpt":"조형 적용","cancel_sculpt":"조형 취소","neighbor_ring":"이웃 청크 범위",
        "lock_boundary":"바깥 경계 잠금","sculpt_hint":"고정 토폴로지 Sculpt 브러시만 사용하세요. Dyntopo와 Remesh는 적용할 수 없습니다.",
        "asset_library":"조형물 라이브러리","registry":"에셋 목록 파일","source_collection":"원본 컬렉션",
        "asset_id":"에셋 ID","name_ko":"한국어 이름","name_en":"영어 이름","category":"분류",
        "ownership":"스트리밍 소유권","save_asset":"에셋 등록 / 갱신","reload":"목록 다시 불러오기",
        "place_cursor":"커서 위치에 배치","place_surface":"표면 배치 도구","search":"검색",
        "align_surface":"표면에 정렬","scale_min":"최소 크기","scale_max":"최대 크기",
        "no_asset":"등록된 조형물 에셋을 선택하세요.","chunk":"청크","region":"리전",
        "vertical_layers":"수직 레이어","layer_height":"레이어 높이","layer_base":"레이어 기준 Z","layer_count":"레이어 개수",
        "active_layer_index":"활성 레이어","layer_from_object":"활성 오브젝트 기준","layer_frame":"레이어로 시점 이동",
        "layer_isolate":"격리 보기","layer_apply_isolate":"격리 적용","layer_show_all":"전체 표시",
        "layer_selection":"선택 항목","layer_snap":"바닥에 스냅","layer_follow_grid":"그리드가 레이어 따라감",
        "layer_bands":"레이어 밴드 표시","layer_lock":"배치를 레이어에 고정",
        "sea_level":"해수면","band_shallow":"표층","band_mid":"중층","band_deep":"심해","show_bands":"수심 밴드 표시","cursor_depth":"커서 수심",
        "goto_chunk":"청크로 이동","bookmark_add":"현재 시점 저장","bookmark_jump":"이동","bookmark_update":"갱신","bookmark_name":"이름",
        "traversal_profile":"이동 방식","player_height":"키","player_radius":"반지름","max_slope":"최대 경사","probe_spacing":"검사 간격","traversal_scope":"범위",
        "entity_catalog_file":"카탈로그 JSON","entity_catalog":"엔티티 카탈로그","placement_kind":"배치 종류","entity_prefab_id":"엔티티 프리팹 ID","entity_kind":"엔티티 종류",
        "entity_persistent":"영구 저장","entity_region_streamed":"리전 스트리밍","entity_replicated":"네트워크 복제",
        "world_grid":"월드 그리드","active_chunk_panel":"활성 청크","chunk_object":"청크 오브젝트","unity_export":"Unity 내보내기","biomes":"바이옴","scatter":"분산 배치","splines":"스플라인","seams":"경계 연결","bake":"베이크","stamps":"스탬프","analysis":"월드 분석","vertex_bake":"버텍스 속성 베이크","terrain_toolkit":"스타일 지형","rock_generator":"스타일 바위 생성기","validate_world":"월드 검사","export_dirty":"변경 청크 내보내기","generate":"생성","apply":"적용","cancel":"취소",
    }
}

def language(scene=None):
    value=getattr(getattr(scene,"worldbuilder_chunks",None),"ui_language","AUTO")
    if value=="KO":return "ko"
    if value=="EN":return "en"
    try:
        import bpy
        locale=(bpy.app.translations.locale or "").lower()
        return "ko" if locale.startswith("ko") else "en"
    except Exception:return "en"

def tr(key,scene=None):return MESSAGES.get(language(scene),MESSAGES["en"]).get(key,key)

_NATIVE={"ko_KR":{("*",english):MESSAGES["ko"].get(key,english) for key,english in MESSAGES["en"].items()}}

def register():
    try:
        import bpy;bpy.app.translations.register(__name__,_NATIVE)
    except (RuntimeError,ValueError):pass

def unregister():
    try:
        import bpy;bpy.app.translations.unregister(__name__)
    except (RuntimeError,ValueError):pass
