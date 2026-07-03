param(
    [string]$Project = "demo/PixelEngine.Demo/PixelEngine.Demo.csproj",
    [string]$Content = "demo/PixelEngine.Demo/content",
    [string]$Artifacts = "artifacts/demo-manual-acceptance-preflight",
    [string]$EvidenceManifestPath = "",
    [switch]$RunScriptedProbes,
    [switch]$AllowBlocked
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot {
    $directory = [System.IO.DirectoryInfo]::new((Get-Location).Path)
    while ($null -ne $directory) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName "PixelEngine.sln")) {
            return $directory.FullName
        }

        $directory = $directory.Parent
    }

    throw "无法从当前目录定位 PixelEngine.sln。"
}

function ConvertTo-RepositoryRelativePath {
    param(
        [string]$Root,
        [string]$Path
    )

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ($resolved.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
        return $resolved.Substring($Root.Length).TrimStart('\', '/')
    }

    return $resolved
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-UInt32BigEndian {
    param([System.IO.BinaryReader]$Reader)

    $bytes = $Reader.ReadBytes(4)
    if ($bytes.Length -ne 4) {
        throw "MP4 box 数据截断，无法读取 uint32。"
    }

    return [uint32]((([uint32]$bytes[0]) -shl 24) -bor (([uint32]$bytes[1]) -shl 16) -bor (([uint32]$bytes[2]) -shl 8) -bor ([uint32]$bytes[3]))
}

function Read-UInt64BigEndian {
    param([System.IO.BinaryReader]$Reader)

    $bytes = $Reader.ReadBytes(8)
    if ($bytes.Length -ne 8) {
        throw "MP4 box 数据截断，无法读取 uint64。"
    }

    $value = [uint64]0
    foreach ($byte in $bytes) {
        $value = ($value -shl 8) -bor [uint64]$byte
    }

    return $value
}

function Read-AsciiString {
    param(
        [System.IO.BinaryReader]$Reader,
        [int]$Length
    )

    $bytes = $Reader.ReadBytes($Length)
    if ($bytes.Length -ne $Length) {
        throw "MP4 box 数据截断，无法读取字符串。"
    }

    return [System.Text.Encoding]::ASCII.GetString($bytes)
}

function Read-Mp4BoxHeader {
    param(
        [System.IO.BinaryReader]$Reader,
        [int64]$ContainerEnd
    )

    if ($Reader.BaseStream.Position + 8 -gt $ContainerEnd) {
        return $null
    }

    $start = [int64]$Reader.BaseStream.Position
    $size = [uint64](Read-UInt32BigEndian -Reader $Reader)
    $type = Read-AsciiString -Reader $Reader -Length 4
    $headerSize = [uint64]8
    if ($size -eq 1) {
        $size = Read-UInt64BigEndian -Reader $Reader
        $headerSize = 16
    }
    elseif ($size -eq 0) {
        $size = [uint64]($ContainerEnd - $start)
    }

    if ($size -lt $headerSize) {
        throw "MP4 box $type size 无效：$size"
    }

    $end = $start + [int64]$size
    if ($end -gt $ContainerEnd) {
        throw "MP4 box $type 越过容器边界：end=$end containerEnd=$ContainerEnd"
    }

    return [pscustomobject]@{
        type = $type
        start = $start
        payloadStart = $start + [int64]$headerSize
        end = $end
        payloadBytes = [int64]$size - [int64]$headerSize
    }
}

function Read-Mp4DurationSeconds {
    param(
        [System.IO.BinaryReader]$Reader,
        [object]$Box
    )

    if ($Box.payloadBytes -lt 20) {
        return 0.0
    }

    $Reader.BaseStream.Seek($Box.payloadStart, [System.IO.SeekOrigin]::Begin) | Out-Null
    $version = $Reader.ReadByte()
    $Reader.BaseStream.Seek(3, [System.IO.SeekOrigin]::Current) | Out-Null
    if ($version -eq 1) {
        if ($Box.payloadBytes -lt 32) {
            return 0.0
        }

        $Reader.BaseStream.Seek(16, [System.IO.SeekOrigin]::Current) | Out-Null
        $timescale = [double](Read-UInt32BigEndian -Reader $Reader)
        $duration = [double](Read-UInt64BigEndian -Reader $Reader)
    }
    else {
        $Reader.BaseStream.Seek(8, [System.IO.SeekOrigin]::Current) | Out-Null
        $timescale = [double](Read-UInt32BigEndian -Reader $Reader)
        $duration = [double](Read-UInt32BigEndian -Reader $Reader)
    }

    if ($timescale -le 0 -or $duration -le 0) {
        return 0.0
    }

    return $duration / $timescale
}

function Read-Mp4MediaInfo {
    param(
        [System.IO.BinaryReader]$Reader,
        [object]$MdiaBox
    )

    $hasVideoHandler = $false
    $durationSeconds = 0.0
    $Reader.BaseStream.Seek($MdiaBox.payloadStart, [System.IO.SeekOrigin]::Begin) | Out-Null
    while ($Reader.BaseStream.Position + 8 -le $MdiaBox.end) {
        $box = Read-Mp4BoxHeader -Reader $Reader -ContainerEnd $MdiaBox.end
        if ($null -eq $box) {
            break
        }

        if ($box.type -eq "hdlr" -and $box.payloadBytes -ge 12) {
            $Reader.BaseStream.Seek($box.payloadStart + 8, [System.IO.SeekOrigin]::Begin) | Out-Null
            $handler = Read-AsciiString -Reader $Reader -Length 4
            if ($handler -eq "vide") {
                $hasVideoHandler = $true
            }
        }
        elseif ($box.type -eq "mdhd") {
            $durationSeconds = [Math]::Max($durationSeconds, (Read-Mp4DurationSeconds -Reader $Reader -Box $box))
        }

        $Reader.BaseStream.Seek($box.end, [System.IO.SeekOrigin]::Begin) | Out-Null
    }

    return [pscustomobject]@{
        hasVideoHandler = $hasVideoHandler
        durationSeconds = $durationSeconds
    }
}

function Read-Mp4TrackInfo {
    param(
        [System.IO.BinaryReader]$Reader,
        [object]$TrakBox
    )

    $hasVideoTrack = $false
    $durationSeconds = 0.0
    $Reader.BaseStream.Seek($TrakBox.payloadStart, [System.IO.SeekOrigin]::Begin) | Out-Null
    while ($Reader.BaseStream.Position + 8 -le $TrakBox.end) {
        $box = Read-Mp4BoxHeader -Reader $Reader -ContainerEnd $TrakBox.end
        if ($null -eq $box) {
            break
        }

        if ($box.type -eq "mdia") {
            $media = Read-Mp4MediaInfo -Reader $Reader -MdiaBox $box
            if ($media.hasVideoHandler) {
                $hasVideoTrack = $true
                $durationSeconds = [Math]::Max($durationSeconds, [double]$media.durationSeconds)
            }
        }

        $Reader.BaseStream.Seek($box.end, [System.IO.SeekOrigin]::Begin) | Out-Null
    }

    return [pscustomobject]@{
        hasVideoTrack = $hasVideoTrack
        durationSeconds = $durationSeconds
    }
}

function Read-Mp4MovieInfo {
    param(
        [System.IO.BinaryReader]$Reader,
        [object]$MoovBox
    )

    $movieDurationSeconds = 0.0
    $videoDurationSeconds = 0.0
    $hasVideoTrack = $false
    $Reader.BaseStream.Seek($MoovBox.payloadStart, [System.IO.SeekOrigin]::Begin) | Out-Null
    while ($Reader.BaseStream.Position + 8 -le $MoovBox.end) {
        $box = Read-Mp4BoxHeader -Reader $Reader -ContainerEnd $MoovBox.end
        if ($null -eq $box) {
            break
        }

        if ($box.type -eq "mvhd") {
            $movieDurationSeconds = [Math]::Max($movieDurationSeconds, (Read-Mp4DurationSeconds -Reader $Reader -Box $box))
        }
        elseif ($box.type -eq "trak") {
            $track = Read-Mp4TrackInfo -Reader $Reader -TrakBox $box
            if ($track.hasVideoTrack) {
                $hasVideoTrack = $true
                $videoDurationSeconds = [Math]::Max($videoDurationSeconds, [double]$track.durationSeconds)
            }
        }

        $Reader.BaseStream.Seek($box.end, [System.IO.SeekOrigin]::Begin) | Out-Null
    }

    return [pscustomobject]@{
        hasVideoTrack = $hasVideoTrack
        durationSeconds = [Math]::Max($movieDurationSeconds, $videoDurationSeconds)
    }
}

function Assert-Mp4VideoEvidence {
    param(
        [string]$Path,
        [string]$Scope,
        [double]$DeclaredDurationSeconds,
        [double]$MinDurationSeconds
    )

    $file = Get-Item -LiteralPath $Path
    if ($file.Length -lt 64) {
        throw "evidence scope $Scope video 文件太小，缺少可解析 MP4/MOV 视频结构：$Path"
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        try {
            $hasFtyp = $false
            $hasMoov = $false
            $hasMdat = $false
            $hasVideoTrack = $false
            $actualDurationSeconds = 0.0
            while ($stream.Position + 8 -le $stream.Length) {
                $box = Read-Mp4BoxHeader -Reader $reader -ContainerEnd $stream.Length
                if ($null -eq $box) {
                    break
                }

                if ($box.type -eq "ftyp") {
                    $hasFtyp = $true
                }
                elseif ($box.type -eq "moov") {
                    $hasMoov = $true
                    $movie = Read-Mp4MovieInfo -Reader $reader -MoovBox $box
                    $hasVideoTrack = $hasVideoTrack -or [bool]$movie.hasVideoTrack
                    $actualDurationSeconds = [Math]::Max($actualDurationSeconds, [double]$movie.durationSeconds)
                }
                elseif ($box.type -eq "mdat" -and $box.payloadBytes -gt 0) {
                    $hasMdat = $true
                }

                $stream.Seek($box.end, [System.IO.SeekOrigin]::Begin) | Out-Null
            }

            if (-not $hasFtyp) {
                throw "evidence scope $Scope video 文件必须包含 MP4/MOV ftyp box，不能用文本或随机字节改名冒充视频：$Path"
            }

            if (-not $hasMoov -or -not $hasVideoTrack -or $actualDurationSeconds -le 0) {
                throw "evidence scope $Scope video 文件必须包含可解析 moov 视频 track 与正 duration，不能只用 ftyp 容器头冒充视频：$Path"
            }

            if (-not $hasMdat) {
                throw "evidence scope $Scope video 文件必须包含非空 mdat 媒体数据，不能只用元数据冒充录屏：$Path"
            }

            if ($actualDurationSeconds + 0.001 -lt $MinDurationSeconds) {
                throw "evidence scope $Scope video 实际 duration $actualDurationSeconds 秒小于要求的 $MinDurationSeconds 秒。"
            }

            if ($DeclaredDurationSeconds -gt $actualDurationSeconds + 0.5) {
                throw "evidence scope $Scope durationSeconds 声明 $DeclaredDurationSeconds 秒超过视频实际 duration $actualDurationSeconds 秒。"
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-VideoEvidence {
    param(
        [string]$Path,
        [string]$Extension,
        [string]$Scope,
        [double]$DeclaredDurationSeconds,
        [double]$MinDurationSeconds
    )

    if ($Extension -in @(".mp4", ".mov")) {
        Assert-Mp4VideoEvidence -Path $Path -Scope $Scope -DeclaredDurationSeconds $DeclaredDurationSeconds -MinDurationSeconds $MinDurationSeconds
        return
    }

    if ($Extension -in @(".mkv", ".webm")) {
        $ffprobe = Get-Command ffprobe -ErrorAction SilentlyContinue
        if ($null -eq $ffprobe) {
            throw "evidence scope $Scope WebM/MKV video 需要 ffprobe 校验真实视频 stream 与 duration，不能只靠 EBML 容器头进入待审：$Path"
        }

        $output = & $ffprobe.Source -v error -select_streams v:0 -show_entries stream=codec_type -show_entries format=duration -of default=noprint_wrappers=1:nokey=0 -- $Path 2>&1
        $outputText = $output -join "`n"
        if ($LASTEXITCODE -ne 0 -or -not ($outputText -match "codec_type=video") -or -not ($outputText -match "duration=([0-9]+(\.[0-9]+)?)")) {
            throw "evidence scope $Scope WebM/MKV video 无法通过 ffprobe 确认真视频 stream 与 duration：$Path"
        }

        $actualDurationSeconds = [double]$Matches[1]
        if ($actualDurationSeconds + 0.001 -lt $MinDurationSeconds) {
            throw "evidence scope $Scope video 实际 duration $actualDurationSeconds 秒小于要求的 $MinDurationSeconds 秒。"
        }

        if ($DeclaredDurationSeconds -gt $actualDurationSeconds + 0.5) {
            throw "evidence scope $Scope durationSeconds 声明 $DeclaredDurationSeconds 秒超过视频实际 duration $actualDurationSeconds 秒。"
        }
    }
}

function Get-BmpFrameEvidence {
    param(
        [string]$Root,
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "脚本化窗口 probe 未写出 framebuffer 截图：$Path"
    }

    $fileInfo = Get-Item -LiteralPath $Path
    if ($fileInfo.Length -lt 54) {
        throw "脚本化窗口 probe 截图不是有效 BMP：$Path"
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        try {
            $signature = $reader.ReadUInt16()
            if ($signature -ne 0x4D42) {
                throw "脚本化窗口 probe 截图缺少 BMP 签名：$Path"
            }

            $stream.Seek(10, [System.IO.SeekOrigin]::Begin) | Out-Null
            $pixelOffset = [int64]$reader.ReadUInt32()
            $stream.Seek(18, [System.IO.SeekOrigin]::Begin) | Out-Null
            $width = $reader.ReadInt32()
            $height = $reader.ReadInt32()
            $stream.Seek(28, [System.IO.SeekOrigin]::Begin) | Out-Null
            $bitsPerPixel = $reader.ReadUInt16()
            if ($width -le 0 -or $height -eq 0 -or $bitsPerPixel -le 0) {
                throw "脚本化窗口 probe 截图 BMP 头无效：width=$width height=$height bitsPerPixel=$bitsPerPixel"
            }

            $compression = $reader.ReadUInt32()
            if ($compression -ne 0) {
                throw "脚本化窗口 probe 截图必须是未压缩 BMP：compression=$compression"
            }

            if ($bitsPerPixel -ne 24 -and $bitsPerPixel -ne 32) {
                throw "脚本化窗口 probe 截图必须是 24/32bpp BMP，实际为 $bitsPerPixel"
            }

            $absHeight = [int]([Math]::Abs([int64]$height))
            $rowStride = [int]([Math]::Floor((($width * $bitsPerPixel) + 31) / 32.0) * 4)
            $pixelBytes = [int64]$rowStride * [int64]$absHeight
            if ($pixelOffset -lt 54 -or $pixelOffset + $pixelBytes -gt $fileInfo.Length) {
                throw "脚本化窗口 probe 截图 BMP 像素数据不完整：offset=$pixelOffset bytes=$pixelBytes file=$($fileInfo.Length)"
            }

            $uniquePixels = [System.Collections.Generic.HashSet[int]]::new()
            $visiblePixelCount = 0
            $hasNonBlankPixel = $false
            $bytesPerPixel = [int]($bitsPerPixel / 8)
            $stream.Seek($pixelOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
            for ($row = 0; $row -lt $absHeight; $row++) {
                $rowBytes = $reader.ReadBytes($rowStride)
                if ($rowBytes.Length -ne $rowStride) {
                    throw "脚本化窗口 probe 截图 BMP 行数据不完整：row=$row"
                }

                for ($x = 0; $x -lt $width; $x++) {
                    $index = $x * $bytesPerPixel
                    $b = [int]$rowBytes[$index]
                    $g = [int]$rowBytes[$index + 1]
                    $r = [int]$rowBytes[$index + 2]
                    $a = if ($bitsPerPixel -eq 32) { [int]$rowBytes[$index + 3] } else { 255 }
                    if ($a -eq 0) {
                        continue
                    }

                    $visiblePixelCount++
                    $pixel = ($a -shl 24) -bor ($r -shl 16) -bor ($g -shl 8) -bor $b
                    $uniquePixels.Add($pixel) | Out-Null
                    if (-not (($r -eq 0 -and $g -eq 0 -and $b -eq 0) -or ($r -eq 255 -and $g -eq 255 -and $b -eq 255))) {
                        $hasNonBlankPixel = $true
                    }
                }
            }

            if ($visiblePixelCount -eq 0) {
                throw "脚本化窗口 probe 截图没有可见像素，不能作为窗口画面证据：$Path"
            }

            if (-not $hasNonBlankPixel) {
                throw "脚本化窗口 probe 截图只有黑/白空白像素，不能作为窗口画面证据：$Path"
            }

            if ($uniquePixels.Count -lt 2) {
                throw "脚本化窗口 probe 截图近似纯色，不能作为窗口画面证据：$Path"
            }

            [pscustomobject]@{
                path = ConvertTo-RepositoryRelativePath -Root $Root -Path $Path
                sha256 = Get-FileSha256 -Path $Path
                bytes = [int64]$fileInfo.Length
                width = [int]$width
                height = $absHeight
                bitsPerPixel = [int]$bitsPerPixel
                uniqueVisiblePixels = [int]$uniquePixels.Count
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ManualScopes {
    @(
        [pscustomobject]@{
            scope = "controlFeelReport"
            kind = "report"
            checklist = @("runJumpWallKick", "sandPileTraversal", "rigidOwnedStanding")
            criteria = @{
                runJumpWallKick = "报告必须说明真实键盘输入下跑、跳、蹬墙均可控，且没有卡死或帧率导致速度漂移。"
                sandPileTraversal = "报告必须说明玩家站在或穿过 settled 沙堆斜面时仍能移动、跳跃，且没有陷入粉末堆。"
                rigidOwnedStanding = "报告必须说明玩家站在 RigidOwned 刚体 stamp 像素上不穿透、不抖动，并能离开。"
            }
            title = "角色跑/跳/蹬墙、站在 settled 沙堆与 RigidOwned 刚体 stamp 像素上不穿不陷"
        },
        [pscustomobject]@{
            scope = "materialBrushAndReactionVideo"
            kind = "video"
            minDurationSeconds = 10.0
            checklist = @("realMouseWheelDigits", "sandWaterOilGasObserved", "reactionTemperatureObserved")
            criteria = @{
                realMouseWheelDigits = "视频必须展示真实鼠标、滚轮和数字键切换材质后在正确世界坐标写入。"
                sandWaterOilGasObserved = "视频必须展示沙堆休止角、水找平、油浮水和气体上升在真实窗口中可见。"
                reactionTemperatureObserved = "视频必须展示至少一种反应和温度相变的视觉结果，并说明是否有异常残留。"
            }
            title = "真实鼠标/滚轮/数字键操作材质笔刷，并观察沙堆休止角、水找平、油浮水、气体上升、反应和温度相变视觉质量"
        },
        [pscustomobject]@{
            scope = "rigidBodyGameplayVideo"
            kind = "video"
            minDurationSeconds = 10.0
            checklist = @("pushAndImpact", "digBridgeCollapse", "continuedDamage")
            criteria = @{
                pushAndImpact = "视频必须展示玩家或场景物体推动、碰撞动态刚体，且碰撞反馈可见。"
                digBridgeCollapse = "视频必须展示挖断木桥或结构后独立连通块转为刚体并掉落。"
                continuedDamage = "视频必须展示掉落刚体继续被挖、烧或酸蚀破坏，且没有长期悬空静态块。"
            }
            title = "真实窗口推动/被砸、挖断木桥转刚体、继续挖/烧/酸蚀破碎、metal 近熔岩熔化坍塌"
        },
        [pscustomobject]@{
            scope = "particleLightingVideo"
            kind = "video"
            minDurationSeconds = 10.0
            checklist = @("particlesVisible", "bloomFogLighting", "noParticleLeak")
            criteria = @{
                particlesVisible = "视频必须展示血、碎屑、发光火花或爆炸粒子在真实窗口中清晰出现并消散。"
                bloomFogLighting = "视频必须展示 bloom、fog reveal 或点光照明随玩法事件变化。"
                noParticleLeak = "视频必须说明长时间玩法后没有持续残留未知点、粒子尾迹或泄漏迹象。"
            }
            title = "血/碎屑/发光火花、爆炸推动邻近刚体、bloom/fog/mining lighting 视觉质量和长时间玩法无粒子泄漏"
        },
        [pscustomobject]@{
            scope = "audioListeningReport"
            kind = "report"
            checklist = @("materialImpacts", "ambientAndReaction", "spatialMix")
            criteria = @{
                materialImpacts = "报告必须说明 sand、stone、wood、metal impact 与 splash 等材质音效均能听到。"
                ambientAndReaction = "报告必须说明 fire/lava ambient、反应音、爆炸破碎、玩家/UI/通关音效触发正常。"
                spatialMix = "报告必须说明真实设备上的空间定位、音量混合和限频效果是否可接受。"
            }
            title = "真实设备听感与空间感：impact、splash、ambient、反应音、爆炸/破碎、玩家/UI/通关音效"
        },
        [pscustomobject]@{
            scope = "fullRoutePlaythroughVideo"
            kind = "video"
            minDurationSeconds = 30.0
            checklist = @("routeCompleted", "materialsReactionsBodiesShown", "audioLightingHudShown")
            criteria = @{
                routeCompleted = "视频必须展示从出生点用至少一种解法抵达出口并触发通关状态。"
                materialsReactionsBodiesShown = "视频必须在路线中展示材质、反应、刚体破坏或坍塌参与解法。"
                audioLightingHudShown = "视频必须展示路线中的音频、光照或 HUD 状态，并能看清通关反馈。"
            }
            title = "从出生点用至少一种解法完整抵达出口，贯穿材质/反应/刚体/粒子/光照/音频"
        },
        [pscustomobject]@{
            scope = "hudMenuEditorVideo"
            kind = "video"
            minDurationSeconds = 10.0
            checklist = @("hudReadable", "menuButtonsClicked", "editorDockspaceOpened")
            criteria = @{
                hudReadable = "视频必须展示 HUD 文本、材质、性能行和状态信息在真实窗口中可读且不重叠。"
                menuButtonsClicked = "视频必须展示暂停菜单按钮点击，包括继续、重开、退出或叠层切换链路。"
                editorDockspaceOpened = "视频必须展示从菜单打开 Editor dockspace 或相关编辑器窗口，并能正常交互。"
            }
            title = "HUD 像素布局、菜单点击、Editor dockspace 打开、重开、退出请求与叠层切换"
        },
        [pscustomobject]@{
            scope = "hotReloadWindowReport"
            kind = "report"
            checklist = @("behaviourSourceEdited", "alcReloadObserved", "statePreserved")
            criteria = @{
                behaviourSourceEdited = "报告必须说明真实开发态窗口运行时修改了某个 Behaviour 源码文件。"
                alcReloadObserved = "报告必须说明 Roslyn 编译和 ALC 热重载被观察到且旧脚本实例被替换。"
                statePreserved = "报告必须说明热重载后场景、世界或关键脚本状态保留，没有重启丢失。"
            }
            title = "开发态真实窗口修改 Behaviour 后 Roslyn + ALC 热重载，场景与世界状态保留"
        }
    )
}

function Get-JsonPropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) {
            return $Object[$Name]
        }

        return $null
    }

    $property = $Object.PSObject.Properties | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-RequiredJsonString {
    param(
        [object]$Object,
        [string]$Name,
        [string]$Scope
    )

    $value = Get-JsonPropertyValue -Object $Object -Name $Name
    if ([string]::IsNullOrWhiteSpace([string]$value)) {
        throw "evidence scope $Scope 缺少 $Name。"
    }

    return [string]$value
}

function Assert-ManualEvidenceMetadata {
    param(
        [object]$Entry,
        [object]$ScopeDefinition,
        [string]$ResolvedPath
    )

    $scope = [string]$ScopeDefinition.scope
    $expectedKind = [string]$ScopeDefinition.kind
    $kind = Get-RequiredJsonString -Object $Entry -Name "kind" -Scope $scope
    if ($kind -ne $expectedKind) {
        throw "evidence scope $scope kind 必须为 $expectedKind，实际为 $kind。"
    }

    $reviewer = Get-RequiredJsonString -Object $Entry -Name "reviewer" -Scope $scope
    if ($reviewer.Length -lt 2) {
        throw "evidence scope $scope reviewer 过短。"
    }

    $capturedAt = Get-RequiredJsonString -Object $Entry -Name "capturedAt" -Scope $scope
    $parsedCapturedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($capturedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsedCapturedAt)) {
        throw "evidence scope $scope capturedAt 不是可解析时间：$capturedAt"
    }

    $notes = Get-RequiredJsonString -Object $Entry -Name "notes" -Scope $scope
    if ($notes.Trim().Length -lt 20) {
        throw "evidence scope $scope notes 至少需要 20 个字符，说明观察结论与残余风险。"
    }

    $checklist = Get-JsonPropertyValue -Object $Entry -Name "checklist"
    if ($null -eq $checklist) {
        throw "evidence scope $scope 缺少 checklist。"
    }

    foreach ($key in @($ScopeDefinition.checklist)) {
        $value = Get-JsonPropertyValue -Object $checklist -Name $key
        if ($null -eq $value -or -not ($value -is [bool]) -or -not [bool]$value) {
            throw "evidence scope $scope checklist.$key 必须为 true。"
        }
    }

    $criteria = Get-JsonPropertyValue -Object $Entry -Name "criteria"
    if ($null -eq $criteria) {
        throw "evidence scope $scope 缺少 criteria。"
    }

    foreach ($key in @($ScopeDefinition.checklist)) {
        $criterion = [string](Get-JsonPropertyValue -Object $criteria -Name $key)
        if ([string]::IsNullOrWhiteSpace($criterion) -or $criterion.Trim().Length -lt 20) {
            throw "evidence scope $scope criteria.$key 至少需要 20 个字符。"
        }
    }

    $extension = [System.IO.Path]::GetExtension($ResolvedPath).ToLowerInvariant()
    if ($expectedKind -eq "video") {
        $allowedVideo = @(".mp4", ".mov", ".mkv", ".webm")
        if ($extension -notin $allowedVideo) {
            throw "evidence scope $scope 是 video，文件扩展名必须为 $($allowedVideo -join ', ')。"
        }

        $duration = Get-JsonPropertyValue -Object $Entry -Name "durationSeconds"
        if ($null -eq $duration) {
            throw "evidence scope $scope 是 video，缺少 durationSeconds。"
        }

        $durationValue = [double]$duration
        $minDuration = [double](Get-JsonPropertyValue -Object $ScopeDefinition -Name "minDurationSeconds")
        if ($durationValue -lt $minDuration) {
            throw "evidence scope $scope durationSeconds 必须至少为 $minDuration 秒。"
        }

        Assert-VideoEvidence -Path $ResolvedPath -Extension $extension -Scope $scope -DeclaredDurationSeconds $durationValue -MinDurationSeconds $minDuration
    }
    elseif ($expectedKind -eq "report") {
        $allowedReport = @(".md", ".txt", ".pdf")
        if ($extension -notin $allowedReport) {
            throw "evidence scope $scope 是 report，文件扩展名必须为 $($allowedReport -join ', ')。"
        }
    }
    else {
        throw "未知人工证据 kind：$expectedKind"
    }
}

function ConvertFrom-ScriptedProbeSummary {
    param([string]$Summary)

    $values = [ordered]@{}
    $normalized = $Summary
    $prefix = "脚本化窗口输入摘要："
    if ($normalized.StartsWith($prefix, [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring($prefix.Length)
    }

    $normalized = $normalized.Trim().TrimEnd('。')
    $matches = [regex]::Matches($normalized, '(?<key>[A-Za-z0-9_]+)=(?<value>.*?)(?=, [A-Za-z0-9_]+=|$)')
    foreach ($match in $matches) {
        $values[$match.Groups["key"].Value] = $match.Groups["value"].Value.Trim()
    }

    return $values
}

function Get-ScriptedProbeSummaryValue {
    param(
        [System.Collections.IDictionary]$Values,
        [string]$ProbeName,
        [string]$Key
    )

    if (-not $Values.Contains($Key)) {
        throw "scripted window probe $ProbeName 摘要缺少语义字段：$Key。"
    }

    return [string]$Values[$Key]
}

function Assert-ScriptedProbeBoolean {
    param(
        [System.Collections.IDictionary]$Values,
        [string]$ProbeName,
        [string]$Key,
        [bool]$Expected
    )

    $actual = Get-ScriptedProbeSummaryValue -Values $Values -ProbeName $ProbeName -Key $Key
    if (-not [string]::Equals($actual, $Expected.ToString(), [StringComparison]::OrdinalIgnoreCase)) {
        throw "scripted window probe $ProbeName 摘要字段 $Key 必须为 $Expected，实际为 $actual。"
    }
}

function Assert-ScriptedProbeNumberAtLeast {
    param(
        [System.Collections.IDictionary]$Values,
        [string]$ProbeName,
        [string]$Key,
        [double]$Minimum
    )

    $actualText = Get-ScriptedProbeSummaryValue -Values $Values -ProbeName $ProbeName -Key $Key
    $actual = [double]0
    if (-not [double]::TryParse($actualText, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$actual)) {
        throw "scripted window probe $ProbeName 摘要字段 $Key 不是数字：$actualText。"
    }

    if ($actual -lt $Minimum) {
        throw "scripted window probe $ProbeName 摘要字段 $Key 必须 >= $Minimum，实际为 $actual。"
    }
}

function Assert-ScriptedProbeNumberBelow {
    param(
        [System.Collections.IDictionary]$Values,
        [string]$ProbeName,
        [string]$Key,
        [double]$MaximumExclusive
    )

    $actualText = Get-ScriptedProbeSummaryValue -Values $Values -ProbeName $ProbeName -Key $Key
    $actual = [double]0
    if (-not [double]::TryParse($actualText, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$actual)) {
        throw "scripted window probe $ProbeName 摘要字段 $Key 不是数字：$actualText。"
    }

    if ($actual -ge $MaximumExclusive) {
        throw "scripted window probe $ProbeName 摘要字段 $Key 必须 < $MaximumExclusive，实际为 $actual。"
    }
}

function Assert-ScriptedProbeRangeWidthAtLeast {
    param(
        [System.Collections.IDictionary]$Values,
        [string]$ProbeName,
        [string]$Key,
        [double]$MinimumWidth
    )

    $actualText = Get-ScriptedProbeSummaryValue -Values $Values -ProbeName $ProbeName -Key $Key
    $match = [regex]::Match($actualText, '^\((?<min>-?\d+(?:\.\d+)?),(?<max>-?\d+(?:\.\d+)?)\)$')
    if (-not $match.Success) {
        throw "scripted window probe $ProbeName 摘要字段 $Key 不是范围：$actualText。"
    }

    $min = [double]::Parse($match.Groups["min"].Value, [Globalization.CultureInfo]::InvariantCulture)
    $max = [double]::Parse($match.Groups["max"].Value, [Globalization.CultureInfo]::InvariantCulture)
    $width = $max - $min
    if ($width -lt $MinimumWidth) {
        throw "scripted window probe $ProbeName 摘要字段 $Key 宽度必须 >= $MinimumWidth，实际为 $width。"
    }
}

function Assert-ScriptedProbeSummarySemantics {
    param(
        [string]$Name,
        [System.Collections.IDictionary]$Values
    )

    switch ($Name) {
        "playable-world" {
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "playable_shots" -Minimum 1
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "max_particles" -Minimum 1
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "frame_samples" -Minimum 120
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "camera_samples" -Minimum 1
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "player_ground_samples" -Minimum 1
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "player_air_samples" -Minimum 1
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "player_left_ground" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "player_air_control" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "camera_followed" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "render_camera_synced" -Expected $true
        }
        "main" {
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "frame_samples" -Minimum 60
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "max_lights" -Minimum 1
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "player_air_samples" -Minimum 1
        }
        "route-attempt" {
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "frame_samples" -Minimum 120
            Assert-ScriptedProbeRangeWidthAtLeast -Values $Values -ProbeName $Name -Key "player_x_range" -MinimumWidth 10
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "render_camera_synced" -Expected $true
        }
        "goal" {
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "goal_reached" -Expected $true
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "max_particles" -Minimum 1
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "max_lights" -Minimum 1
        }
        "health" {
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "spawn_probe" -Expected $true
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "damage_events" -Minimum 1
            Assert-ScriptedProbeNumberBelow -Values $Values -ProbeName $Name -Key "player_health" -MaximumExclusive 100
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "max_particles" -Minimum 1
        }
        "camera" {
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "camera_followed" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "render_camera_synced" -Expected $true
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "camera_samples" -Minimum 1
        }
        "reaction" {
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "reactions_observed" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "phase_transitions_observed" -Expected $true
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "max_particles" -Minimum 1
        }
        "audio" {
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "audio_probe_one_shot_played" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "audio_probe_ambient_activated" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "audio_probe_limited" -Expected $true
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "audio_probe_max_played" -Minimum 1
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "audio_probe_max_dropped" -Minimum 1
        }
        "particle-light" {
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "particle_light_probe_spawned" -Minimum 96
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "particle_light_probe_depleted" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "particle_light_probe_light_observed" -Expected $true
            Assert-ScriptedProbeBoolean -Values $Values -ProbeName $Name -Key "particle_light_probe_lighting_synced" -Expected $true
            Assert-ScriptedProbeNumberAtLeast -Values $Values -ProbeName $Name -Key "max_lights" -Minimum 1
        }
        default {
            throw "未知 scripted window probe 名称，缺少语义校验：$Name。"
        }
    }
}

function Invoke-ScriptedProbe {
    param(
        [string]$Name,
        [int]$Ticks,
        [string]$Scene,
        [string[]]$RequiredSummaryMarkers,
        [string]$Root,
        [string]$OutputRoot,
        [string[]]$ExtraArguments = @()
    )

    $directory = Join-Path $OutputRoot $Name
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $stdoutPath = Join-Path $directory "stdout.txt"
    $stderrPath = Join-Path $directory "stderr.txt"
    $capturePath = Join-Path $directory "capture.bmp"
    $logDirectory = Join-Path $directory "logs"

    $arguments = @(
        "run",
        "--project", $Project,
        "-c", "Release",
        "--no-build",
        "--",
        "--no-hot-reload",
        "--window-ticks", $Ticks.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--scripted-window-demo",
        "--content", $Content,
        "--scene", $Scene,
        "--capture-frame", $capturePath,
        "--log-dir", $logDirectory
    )
    foreach ($argument in $ExtraArguments) {
        $arguments += $argument
    }

    $process = Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $Root -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $stdout = Get-Content -LiteralPath $stdoutPath -Raw
    $summary = ($stdout -split "`r?`n" | Where-Object { $_.StartsWith("脚本化窗口输入摘要：", [StringComparison]::Ordinal) } | Select-Object -Last 1)
    if ($process.ExitCode -ne 0) {
        throw "scripted window probe $Name 退出码为 $($process.ExitCode)。"
    }

    if ([string]::IsNullOrWhiteSpace($summary)) {
        throw "scripted window probe $Name 未输出脚本化窗口输入摘要。"
    }

    foreach ($marker in $RequiredSummaryMarkers) {
        if (-not $summary.Contains($marker, [StringComparison]::Ordinal)) {
            throw "scripted window probe $Name 摘要缺少标记：$marker。摘要：$summary"
        }
    }

    $summaryValues = ConvertFrom-ScriptedProbeSummary -Summary $summary
    Assert-ScriptedProbeSummarySemantics -Name $Name -Values $summaryValues

    $capture = Get-BmpFrameEvidence -Root $Root -Path $capturePath

    [pscustomobject]@{
        name = $Name
        scene = $Scene
        ticks = $Ticks
        stdout = ConvertTo-RepositoryRelativePath -Root $Root -Path $stdoutPath
        stderr = ConvertTo-RepositoryRelativePath -Root $Root -Path $stderrPath
        capture = $capture.path
        captureSha256 = $capture.sha256
        captureBytes = $capture.bytes
        captureWidth = $capture.width
        captureHeight = $capture.height
        captureBitsPerPixel = $capture.bitsPerPixel
        captureUniqueVisiblePixels = $capture.uniqueVisiblePixels
        required = $RequiredSummaryMarkers
        summary = $summary
    }
}

function Read-EvidenceManifest {
    param(
        [string]$Root,
        [string]$ManifestPath
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "人工验收 evidence manifest 不存在：$ManifestPath"
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "Demo manual acceptance manifest schemaVersion 必须为 1。"
    }

    $reviewSessionId = [string](Get-JsonPropertyValue -Object $manifest -Name "reviewSessionId")
    if ([string]::IsNullOrWhiteSpace($reviewSessionId)) {
        throw "evidence manifest 缺少 reviewSessionId。"
    }

    $gitCommit = [string](Get-JsonPropertyValue -Object $manifest -Name "gitCommit")
    if ([string]::IsNullOrWhiteSpace($gitCommit)) {
        throw "evidence manifest 缺少 gitCommit。"
    }

    $scopeDefinitions = @(Get-ManualScopes)
    $requiredScopes = @($scopeDefinitions | ForEach-Object { $_.scope })
    $scopeByName = @{}
    foreach ($definition in $scopeDefinitions) {
        $scopeByName[[string]$definition.scope] = $definition
    }
    $entries = @($manifest.evidence)
    $scopes = @{}
    foreach ($entry in $entries) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.scope)) {
            throw "evidence entry 缺少 scope。"
        }

        $scope = [string]$entry.scope
        if ($scope -notin $requiredScopes) {
            throw "未知 evidence scope：$scope"
        }

        if ($scopes.ContainsKey($scope)) {
            throw "重复 evidence scope：$scope"
        }

        $scopes[$scope] = $entry
    }

    $missing = @($requiredScopes | Where-Object { -not $scopes.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        return [pscustomobject]@{
            status = "blocked_missing_manual_scope_evidence"
            missing = $missing
            evidence = @()
        }
    }

    $evidence = [System.Collections.Generic.List[object]]::new()
    foreach ($scope in $requiredScopes) {
        $entry = $scopes[$scope]
        if ([string]::IsNullOrWhiteSpace([string]$entry.path)) {
            throw "evidence scope $scope 缺少 path。"
        }

        $path = [string]$entry.path
        if (-not [System.IO.Path]::IsPathRooted($path)) {
            $path = Join-Path $Root $path
        }

        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "evidence scope $scope 指向文件不存在：$path"
        }

        Assert-ManualEvidenceMetadata -Entry $entry -ScopeDefinition $scopeByName[$scope] -ResolvedPath $path

        $entryReviewSessionId = [string](Get-JsonPropertyValue -Object $entry -Name "reviewSessionId")
        if ([string]::IsNullOrWhiteSpace($entryReviewSessionId)) {
            throw "evidence scope $scope 缺少 reviewSessionId。"
        }

        if (-not [string]::Equals($entryReviewSessionId, $reviewSessionId, [StringComparison]::Ordinal)) {
            throw "evidence scope $scope reviewSessionId 必须为 $reviewSessionId，实际为 $entryReviewSessionId。"
        }

        $entryGitCommit = [string](Get-JsonPropertyValue -Object $entry -Name "gitCommit")
        if ([string]::IsNullOrWhiteSpace($entryGitCommit)) {
            throw "evidence scope $scope 缺少 gitCommit。"
        }

        if (-not [string]::Equals($entryGitCommit, $gitCommit, [StringComparison]::OrdinalIgnoreCase)) {
            throw "evidence scope $scope gitCommit 必须为 $gitCommit，实际为 $entryGitCommit。"
        }

        $hashProperty = $entry.PSObject.Properties | Where-Object { $_.Name -eq "sha256" } | Select-Object -First 1
        $declaredHash = if ($null -eq $hashProperty) { "" } else { [string]$hashProperty.Value }
        if ([string]::IsNullOrWhiteSpace($declaredHash)) {
            throw "evidence scope $scope 缺少 sha256。"
        }

        $actualHash = Get-FileSha256 -Path $path
        $expectedHash = $declaredHash.Trim().ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "evidence scope $scope sha256 不匹配：expected=$expectedHash actual=$actualHash"
        }

        $evidence.Add([pscustomobject]@{
            scope = $scope
            path = ConvertTo-RepositoryRelativePath -Root $Root -Path $path
            sha256 = $actualHash
        })
    }

    [pscustomobject]@{
        status = "manual_evidence_attached_pending_review"
        missing = @()
        evidence = @($evidence)
    }
}

function Write-ManualAcceptanceReport {
    param(
        [string]$Path,
        [string]$Status,
        [int]$ExitCode,
        [object[]]$Evidence,
        [object[]]$Missing,
        [object[]]$Issues,
        [object[]]$ProbeRuns
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Demo 真实窗口人工验收预检")
    $lines.Add("")
    $lines.Add("status: $Status")
    $lines.Add("exit_code: $ExitCode")
    $lines.Add("")
    $lines.Add("## 说明")
    $lines.Add("")
    $lines.Add('该脚本只收集 plan/13 真实窗口人工验收证据。`scripted_probe_only` 与 `manual_evidence_attached_pending_review` 都不是验收通过状态；plan/13 的 `[!]` 只有在人工确认视觉、听感、手感、完整路线和热重载体验后才能改为 `[x]`。')
    $lines.Add("")
    $lines.Add("## 人工验收 scope")
    $lines.Add("")
    foreach ($scope in Get-ManualScopes) {
        $durationText = if ($scope.kind -eq "video") { " minDurationSeconds=$($scope.minDurationSeconds)" } else { "" }
        $lines.Add(("- {0} [{1}]{2}: {3}" -f $scope.scope, $scope.kind, $durationText, $scope.title))
        $lines.Add(("  checklist: {0}" -f (($scope.checklist) -join ", ")))
        foreach ($key in @($scope.checklist)) {
            $lines.Add(("  criteria.{0}: {1}" -f $key, (Get-JsonPropertyValue -Object $scope.criteria -Name $key)))
        }
    }
    $lines.Add("")

    if ($ProbeRuns.Count -gt 0) {
        $lines.Add("## 机器 probe")
        $lines.Add("")
        foreach ($run in $ProbeRuns) {
            $lines.Add("### $($run.name)")
            $lines.Add("")
            $lines.Add("scene: $($run.scene)")
            $lines.Add("ticks: $($run.ticks)")
            $lines.Add(('stdout: `{0}`' -f $run.stdout))
            $lines.Add(('stderr: `{0}`' -f $run.stderr))
            $lines.Add(('capture: `{0}`' -f $run.capture))
            $lines.Add("capture_sha256: $($run.captureSha256)")
            $lines.Add("capture_size: $($run.captureBytes) bytes")
            $lines.Add("capture_dimensions: $($run.captureWidth)x$($run.captureHeight)x$($run.captureBitsPerPixel)")
            $lines.Add("capture_unique_visible_pixels: $($run.captureUniqueVisiblePixels)")
            $lines.Add("required_markers: $($run.required -join '; ')")
            $lines.Add("")
            $lines.Add('```text')
            $lines.Add($run.summary)
            $lines.Add('```')
            $lines.Add("")
        }
    }

    if ($Evidence.Count -gt 0) {
        $lines.Add("## 人工证据")
        $lines.Add("")
        foreach ($item in $Evidence) {
            $lines.Add("- scope=$($item.scope); path=$($item.path); sha256=$($item.sha256)")
        }
        $lines.Add("")
    }

    if ($Missing.Count -gt 0) {
        $lines.Add("## 缺失 scope")
        $lines.Add("")
        foreach ($scope in $Missing) {
            $lines.Add("- $scope")
        }
        $lines.Add("")
    }

    if ($Issues.Count -gt 0) {
        $lines.Add("## 证据问题")
        $lines.Add("")
        foreach ($issue in $Issues) {
            $lines.Add("- $issue")
        }
        $lines.Add("")
    }

    [System.IO.File]::WriteAllLines($Path, $lines, [System.Text.UTF8Encoding]::new($false))
}

$root = Resolve-RepositoryRoot
$artifactRoot = if ([System.IO.Path]::IsPathRooted($Artifacts)) { $Artifacts } else { Join-Path $root $Artifacts }
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$reportPath = Join-Path $artifactRoot "demo-manual-acceptance-preflight.md"
$status = "blocked_missing_manual_evidence"
$exitCode = 2
$evidence = @()
$missing = @()
$issues = @()
$probeRuns = [System.Collections.Generic.List[object]]::new()

if ($RunScriptedProbes) {
    $probeRoot = Join-Path $artifactRoot "scripted-probes"
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
    $probeRuns.Add((Invoke-ScriptedProbe -Name "playable-world" -Ticks 240 -Scene "scenes/playable-world.scene" -RequiredSummaryMarkers @("player_visual=present", "player_visual_overlays=", "playable_shots=", "particles=", "fps=", "frame_ms=", "frame_p99_ms=", "frame_low1_fps=", "frame_jitter_ms=", "frame_samples=", "sim_hz=", "camera_followed=True", "render_camera_synced=True", "render_camera=", "720x480", "player_left_ground=True", "player_air_control=True", "player_ground_samples=", "player_air_samples=", "max_particles=", "hud_blocked=none") -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "main" -Ticks 80 -Scene "scenes/lava-mine.scene" -RequiredSummaryMarkers @("brush_material=", "brush_radius=", "painted_material=", "explosions=", "max_particles=", "max_lights=", "max_physics_destroyed=", "hud_blocked=none") -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "route-attempt" -Ticks 1500 -Scene "scenes/lava-mine.scene" -RequiredSummaryMarkers @("pause_open=False", "hud_blocked=none", "render_camera_synced=True", "goal_reached=", "player_x_range=", "frame_samples=") -Root $root -OutputRoot $probeRoot -ExtraArguments @("--scripted-window-route")))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "goal" -Ticks 40 -Scene "scenes/lava-mine-goal-probe.scene" -RequiredSummaryMarkers @("goal_reached=True", "player_center_material=13", "max_particles=", "max_lights=") -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "health" -Ticks 80 -Scene "scenes/lava-mine-health-probe.scene" -RequiredSummaryMarkers @("spawn_probe=True", "damage_events=", "player_health=", "player_center_material=") -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "camera" -Ticks 80 -Scene "scenes/lava-mine-camera-probe.scene" -RequiredSummaryMarkers @("camera_followed=True", "render_camera_synced=True", "camera_zoom=4.00", "camera_samples=") -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "reaction" -Ticks 180 -Scene "scenes/lava-mine-reaction-probe.scene" -RequiredSummaryMarkers @("reaction_probe_initialized=True", "reactions_observed=True", "phase_transitions_observed=True", "lava_water=True", "sand_glassed=True") -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "audio" -Ticks 30 -Scene "scenes/lava-mine-audio-probe.scene" -RequiredSummaryMarkers @("audio_probe_one_shot_played=True", "audio_probe_ambient_activated=True", "audio_probe_limited=True", "audio_probe_max_dropped=64") -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "particle-light" -Ticks 120 -Scene "scenes/lava-mine-particle-light-probe.scene" -RequiredSummaryMarkers @("particle_light_probe_spawned=96", "particle_light_probe_lifetime_kill=True", "particle_light_probe_depleted=True", "particle_light_probe_light_observed=True", "particle_light_probe_lighting_synced=True") -Root $root -OutputRoot $probeRoot))
    $status = "scripted_probe_only"
}

if (-not [string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    $manifestPath = if ([System.IO.Path]::IsPathRooted($EvidenceManifestPath)) { $EvidenceManifestPath } else { Join-Path $root $EvidenceManifestPath }
    try {
        $manifestResult = Read-EvidenceManifest -Root $root -ManifestPath $manifestPath
        $status = $manifestResult.status
        $evidence = @($manifestResult.evidence)
        $missing = @($manifestResult.missing)
        if ($missing.Count -gt 0) {
            $exitCode = 5
        }
        else {
            $exitCode = 2
        }
    }
    catch {
        $status = "blocked_invalid_manual_evidence"
        $evidence = @()
        $missing = @()
        $issues = @("人工验收 evidence manifest 无效：$($_.Exception.Message)")
        $exitCode = 5
    }
}

Write-ManualAcceptanceReport -Path $reportPath -Status $status -ExitCode $exitCode -Evidence $evidence -Missing $missing -Issues $issues -ProbeRuns @($probeRuns)
Write-Host "Demo manual acceptance preflight status: $status"
Write-Host "Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if ($exitCode -ne 0 -and -not $AllowBlocked) {
    [Console]::Error.WriteLine("Demo manual acceptance preflight failed: $status")
    exit $exitCode
}

exit 0
