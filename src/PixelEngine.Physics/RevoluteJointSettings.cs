namespace PixelEngine.Physics;

/// <summary>
/// 通用 revolute joint 参数。锚点由创建 API 单独以世界像素坐标提供。
/// </summary>
/// <param name="EnableMotor">是否启用角速度 motor。</param>
/// <param name="MaxMotorTorque">最大 motor torque，使用 Box2D 物理单位。</param>
/// <param name="MotorSpeedRadians">目标角速度，单位 rad/s。</param>
/// <param name="CollideConnected">相连刚体是否互相碰撞。</param>
/// <param name="BreakForce">约束力超过该 Box2D force 阈值时自动断裂；0 表示不自动断裂。</param>
/// <param name="BreakDistancePixels">线性分离误差超过该像素距离时自动断裂；0 表示不按距离断裂。</param>
public readonly record struct RevoluteJointSettings(
    bool EnableMotor = false,
    float MaxMotorTorque = 0f,
    float MotorSpeedRadians = 0f,
    bool CollideConnected = false,
    float BreakForce = 0f,
    float BreakDistancePixels = 0f);
