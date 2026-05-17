using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening; // 引入DOTween命名空间

public class DiaCursorTik : MonoBehaviour
{
    public float jumpHeight = 1f; // 跳跃高度
    public float jumpDuration = 1f; // 跳跃所需时间
    public int jumpCount = -1; // 跳跃次数，-1表示无限循环

    private void Start()
    {
        // 开始跳跃动画
        Jump();
    }

    void Jump()
    {
        // 使用DOTween的DOMoveY方法使对象在Y轴上移动
        transform.DOMoveY(transform.position.y + jumpHeight, jumpDuration)
            .OnComplete(OnJumpComplete); // 跳跃完成后调用OnJumpComplete方法
    }

    void OnJumpComplete()
    {
        // 跳跃完成后，使对象返回原位
        transform.DOMoveY(transform.position.y - jumpHeight, jumpDuration)
            .SetLoops(jumpCount, LoopType.Yoyo); // 设置循环次数和循环类型
    }
}
