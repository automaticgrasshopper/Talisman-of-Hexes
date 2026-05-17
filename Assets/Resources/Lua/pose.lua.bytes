local poses = {
    ['Laosan'] = {
        ['normal']  = 'HD_Laosan_normal',   -- 默认：病后平静，嘴角若有若无的幽默感
        ['tired']   = 'HD_Laosan_tired',    -- 倦：眼神涣散，更虚弱，急症期使用
        ['amused']  = 'HD_Laosan_amused',   -- 幽默：鬼魂视角下的干笑，眼睛里有点光
        ['pensive'] = 'HD_Laosan_pensive',  -- 沉思：盯着某处，心不在焉
    },
    ['Xiangbei'] = {
        ['normal'] = 'HD_Xiangbei_normal',  -- 默认：麻木，嘴紧，眼神内收
        ['tense']  = 'HD_Xiangbei_tense',   -- 绷：情绪压着快压不住，下颌紧
        ['sad']    = 'HD_Xiangbei_sad',     -- 破防：少数真正露出来的时刻
    },
    ['Yajie'] = {
        ['normal'] = 'HD_Yajie_normal',     -- 默认：焦虑但撑着，嘴抿着
        ['tired']  = 'HD_Yajie_tired',      -- 倦：守灵之后，眼睛红肿
        ['upset']  = 'HD_Yajie_upset',      -- 崩：说不出话或已经哭出来
    },
    ['Hugong'] = {
        ['normal'] = 'HD_Hugong_normal',    -- 默认：职业化温和，随意自然
    },
    ['Laojiu'] = {
        ['normal'] = 'HD_Laojiu_normal',    -- 默认：沉静，可靠，扛事的样子
    },
}

function get_all_poses_by_name(obj_name)
    local ret = {}
    if poses[obj_name] then
        for k, _ in pairs(poses[obj_name]) do
            ret[#ret + 1] = k
        end
        table.sort(ret)
    end
    return ret
end

function get_pose_by_name(obj_name, pose_name)
    -- Not alias
    if string.find(pose_name, '+') then
        return pose_name
    end

    local pose = poses[obj_name] and poses[obj_name][pose_name]
    if pose then
        return pose
    end

    warn('Unknown pose ' .. dump(pose_name) .. ' for composite sprite ' .. dump(obj_name))
    return pose_name
end

function get_pose(obj, pose_name)
    return get_pose_by_name(obj.luaGlobalName, pose_name)
end
