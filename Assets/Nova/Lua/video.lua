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

-- 视频循环：循环视频选项模式（branch 出现时视频在底层一直循环）
function video_set_loop(b)
    __Nova.videoController:SetLoop(b)
end

-- 让视频画布垫到 UI 底下，选项按钮/倒计时条会浮在视频上方。
function video_to_back()
    __Nova.videoController:SetCanvasOrder(-1)
end

-- 视频画布回到默认 sortingOrder（视频覆盖 UI）。普通过场视频不用调，video_to_back 之后想还原才调。
function video_to_front()
    __Nova.videoController:ResetCanvasOrder()
end

-- 限时选项：起一个 TimeSlider 倒计时，归零时自动选中 timeout_index 对应分支。
-- branch{} 里第一项是 index 0，第二项是 index 1。
function start_choice_timer(seconds, timeout_index)
    __Nova.videoController:StartChoiceTimer(seconds, timeout_index or 0)
end

function stop_choice_timer()
    __Nova.videoController:StopChoiceTimer()
end

-- 进入"视频中显示选项"模式：实例化 VideoButton 容器到视频画布下，
-- 之后的 branch{} 选项会出现在视频上方（同一 Canvas，不会被视频盖住）。
-- 玩家选中后会自动退出此模式并销毁容器。
-- hidden_index（可选）：branch{} 里某个 index 仅用作 start_choice_timer 超时跳转目标，
-- 不生成按钮（玩家看不到）。例如 branch 有 3 项时传 2 表示第 3 项是隐藏 timeout dest。
function video_choice_mode_on(hidden_index)
    __Nova.videoController:EnableVideoChoiceMode(hidden_index or -1)
end

-- 主动退出视频选项模式（玩家未选就需要切场景时用，正常选中后无需手动调）。
function video_choice_mode_off()
    __Nova.videoController:DisableVideoChoiceMode()
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
