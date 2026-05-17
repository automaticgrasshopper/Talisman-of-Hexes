--- require order is important
--- built_in.lua cannot be hot-reloaded, so we use require rather than dofile
require 'built_in'

dofile 'hook'
dofile 'preload'

dofile 'alert'
dofile 'animation'
dofile 'animation_high_level'
dofile 'audio'
dofile 'auto_voice'
dofile 'avatar'
dofile 'checkpoint_helper'
dofile 'dialogue_box'
dofile 'garbage_collection'
dofile 'graphics'
dofile 'input'
dofile 'minigame'
dofile 'pose'
dofile 'script_loader'
dofile 'shader_info'
dofile 'timeline'
dofile 'transition'
dofile 'variables'
dofile 'video'

dofile 'animation_presets'

-- Video subtitle data, keyed by locale name then by 'node_id[.video_id]'.
-- Populated by LuaRuntime.LoadSubtitleData() from Assets/Resources/Subtitles/subtitles_<locale>.txt
-- after this file finishes executing. Looked up at runtime by video_subtitle_apply() in video.lua.
_subtitle_data = {}
