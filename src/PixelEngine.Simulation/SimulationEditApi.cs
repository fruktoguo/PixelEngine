namespace PixelEngine.Simulation;

/// <summary>
/// 基于 <see cref="SimulationKernel" /> 的编辑器 phase [1] 写入与检视门面。
/// </summary>
public sealed class SimulationEditApi(
    SimulationKernel kernel,
    MaterialTable materials,
    TemperatureField? temperature = null,
    IRigidCellOwnershipLookup? rigidOwnership = null) : ISimulationEditApi, ISimulationInspectApi
{
    private readonly SimulationKernel _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly TemperatureField? _temperature = temperature;
    private readonly IRigidCellOwnershipLookup? _rigidOwnership = rigidOwnership;

    /// <inheritdoc />
    public void PaintCell(int worldX, int worldY, ushort material)
    {
        ValidateLiveMaterial(material);
        _kernel.EditCellAtInputPhase(worldX, worldY, material, persistentFlags: 0);
    }

    /// <inheritdoc />
    public void ClearCell(int worldX, int worldY)
    {
        _kernel.ClearCellAtInputPhase(worldX, worldY);
    }

    /// <inheritdoc />
    public void AddTemperature(int worldX, int worldY, float deltaCelsius)
    {
        TemperatureField field = _temperature ?? throw new InvalidOperationException("温度笔刷需要接入 TemperatureField。");
        field.AddHeat(worldX, worldY, deltaCelsius);
        _kernel.MarkDirty(worldX, worldY);
    }

    /// <inheritdoc />
    public void SetTemperature(int worldX, int worldY, float targetCelsius)
    {
        TemperatureField field = _temperature ?? throw new InvalidOperationException("温度笔刷需要接入 TemperatureField。");
        field.AddHeat(worldX, worldY, targetCelsius - field.GetTemperature(worldX, worldY));
        _kernel.MarkDirty(worldX, worldY);
    }

    /// <inheritdoc />
    public bool TryInspectCell(int worldX, int worldY, out SimulationCellInspection inspection)
    {
        return _kernel.TryInspectCell(worldX, worldY, _materials, _temperature, _rigidOwnership, out inspection);
    }

    private void ValidateLiveMaterial(ushort material)
    {
        if (material >= _materials.Count || _materials.IsTombstone(material))
        {
            throw new ArgumentOutOfRangeException(nameof(material), material, "画刷材质必须是 live runtime id。");
        }
    }
}
