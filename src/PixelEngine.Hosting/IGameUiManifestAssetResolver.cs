namespace PixelEngine.Hosting;

/// <summary>
/// 将 .scene 中稳定的 Web Canvas manifest asset id 解析为当前项目内的 manifest 文件路径。
/// Editor 可从资产数据库解析，打包运行时可使用入盘 logical path 作为回退。
/// </summary>
public interface IGameUiManifestAssetResolver
{
    /// <summary>
    /// 尝试解析稳定 manifest asset id。
    /// </summary>
    /// <param name="assetId">稳定资产 id。</param>
    /// <param name="manifestPath">解析出的绝对或相对 content 根路径。</param>
    /// <returns>当前资产数据库仍包含该 id 时返回 true。</returns>
    bool TryResolveManifest(string assetId, out string manifestPath);
}
