namespace Nova
{
    /// <summary>
    /// 死亡结局路由接口。由 FlowChartController（Assembly-CSharp）实现，
    /// 让 GameViewController（Nova 程序集）能在不反向引用 Assembly-CSharp 的前提下接管死亡结局。
    ///
    /// 注意：FlowChartView 不挂在 ViewManager 子层级下，所以 ViewManager.GetController 找不到它。
    /// 改用 DeathRouteRegistry 静态注册：FlowChartController 在 Awake 里把自己注册进去，
    /// GameViewController 通过 DeathRouteRegistry.Current 拿到引用。
    /// </summary>
    public interface IDeathRouteHandler : IViewController
    {
        bool IsDeathNode(string nodeName);
        void PrepareDeathEnding(string deathNodeName);
    }

    /// <summary>
    /// 死亡路由静态注册表。一个场景里只允许一个 FlowChartController 同时充当死亡路由处理者。
    /// </summary>
    public static class DeathRouteRegistry
    {
        public static IDeathRouteHandler Current;
    }
}
