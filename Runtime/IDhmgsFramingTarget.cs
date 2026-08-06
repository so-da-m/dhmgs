using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// FixedAvatarCamera가 비출 대상. Pivot은 프레이밍 기준 Transform(준비 안 됐으면 null).
	/// 컨트롤러(활성 아바타 pelvis), HumanoidPreview(휴머노이드 hips) 등이 구현한다.
	/// </summary>
	public interface IDhmgsFramingTarget
	{
		Transform Pivot { get; }
	}
}
