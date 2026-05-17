function video(video_name)
    __Nova.videoController:SetVideo(video_name)
end

function video_hide()
    __Nova.videoController:ClearVideo()
    schedule_gc()
end

function video_play()
    __Nova.videoController:Play()
end

function video_duration()
    return __Nova.videoController.duration
end

local function parse_subtitle_time(t)
    if type(t) == 'number' then
        return t
    end
    -- "hh:mm:ss.mmm" or "hh:mm:ss"
    local h, m, s = string.match(t, '^(%d+):(%d+):(%d+%.?%d*)$')
    if h then
        return tonumber(h) * 3600 + tonumber(m) * 60 + tonumber(s)
    end
    -- "mm:ss.mmm" or "mm:ss"
    local m2, s2 = string.match(t, '^(%d+):(%d+%.?%d*)$')
    if m2 then
        return tonumber(m2) * 60 + tonumber(s2)
    end
    return tonumber(t) or 0
end

-- Called from lazy <| |> blocks in scenario scripts, before video_play().
-- Looks up subtitle entries for the current locale from _subtitle_data, then
-- pushes them into the video controller. Subtitle data lives in
-- subtitles_<locale>.txt files (loaded by LuaRuntime.LoadSubtitleData).
-- node_id: scenario node name, e.g. 'ch0_1'
-- id: optional video id within the node, e.g. 'v1' / 'v2'
function video_subtitle_apply(node_id, id)
    __Nova.videoController:ClearSubtitles()
    local locale_key = tostring(Nova.I18n.CurrentLocale)
    local key = id and (node_id .. '.' .. id) or node_id
    local locale_table = _subtitle_data[locale_key]
    if not locale_table then return end
    local entries = locale_table[key]
    if not entries then return end
    for _, e in ipairs(entries) do
        __Nova.videoController:AddSubtitle(
            parse_subtitle_time(e.from),
            parse_subtitle_time(e.to),
            e.text
        )
    end
end
