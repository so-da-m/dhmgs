using Mnnh.Dhmgs;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	/// <summary>
	/// Sample-only UI adapter for an already imported DhmgsMotionAsset.
	/// It deliberately has no download, parsing, or baking responsibilities.
	/// </summary>
	public sealed class DhmgsFullDemoController : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private DhmgsMotionAsset _motion;
		[SerializeField] private bool _playOnStart = true;
		[SerializeField] private bool _loop = true;

		private int _avatarIndex;
		private float _speed = 1f;
		private GUIStyle _titleStyle;
		private GUIStyle _bodyStyle;

		private void Start()
		{
			InitializeAvatar();
			ApplySpeed(_speed);
			if (_playOnStart)
			{
				Play();
			}
		}

		private void OnGUI()
		{
			EnsureStyles();
			GUILayout.BeginArea(new Rect(24f, 24f, 390f, 260f), GUI.skin.box);
			GUILayout.Label("DHMGS Full Demo", _titleStyle);
			GUILayout.Label(
				"Bundled motion asset + package-owned SMPL-X base rigs. No project-local content is required.",
				_bodyStyle);

			GUILayout.Space(8f);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Play", GUILayout.Height(30f)))
			{
				Play();
			}
			if (GUILayout.Button("Stop", GUILayout.Height(30f)))
			{
				Stop();
			}
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("<", GUILayout.Width(44f)))
			{
				SelectAvatar(_avatarIndex - 1);
			}
			GUILayout.Label($"Avatar  {ActiveAvatarName}", GUILayout.ExpandWidth(true));
			if (GUILayout.Button(">", GUILayout.Width(44f)))
			{
				SelectAvatar(_avatarIndex + 1);
			}
			GUILayout.EndHorizontal();

			float nextSpeed = GUILayout.HorizontalSlider(_speed, 0.25f, 2f);
			if (!Mathf.Approximately(nextSpeed, _speed))
			{
				ApplySpeed(nextSpeed);
			}
			GUILayout.Label($"Speed  {_speed:F2}x");
			GUILayout.Label(StatusText);

			GUILayout.Space(5f);
			GUILayout.Label("Import more motion in the Editor: DHMGS > Importer", _bodyStyle);
			GUILayout.EndArea();
		}

		public void Play()
		{
			if (_player == null || _motion == null || !_motion.IsValid)
			{
				Debug.LogError("[DHMGS Sample] Assign a valid player and motion asset.", this);
				return;
			}

			_player.Play(_motion, new DhmgsRealtimeClock(_loop));
			ApplySpeed(_speed);
		}

		public void Stop()
		{
			_player?.Stop();
		}

		private string ActiveAvatarName =>
			_player != null && _player.AvatarCount > 0
				? _player.AvatarName(_avatarIndex)
				: "None";

		private string StatusText
		{
			get
			{
				if (_player == null || _motion == null)
				{
					return "No motion assigned";
				}

				string state = _player.IsPlaying ? "Playing" : "Stopped";
				return $"{state}  |  Frame {_player.CurrentFrame + 1}/{_motion.frameCount}  |  {_motion.fps:F2} fps";
			}
		}

		private void InitializeAvatar()
		{
			if (_player == null || _player.AvatarCount == 0)
			{
				return;
			}

			_avatarIndex = Mathf.Clamp(_player.ActiveAvatar, 0, _player.AvatarCount - 1);
			SelectAvatar(_avatarIndex);
		}

		private void SelectAvatar(int index)
		{
			if (_player == null || _player.AvatarCount == 0)
			{
				return;
			}

			_avatarIndex = (index + _player.AvatarCount) % _player.AvatarCount;
			_player.SetAvatarIndex(_avatarIndex);
		}

		private void ApplySpeed(float speed)
		{
			_speed = Mathf.Clamp(speed, 0.25f, 2f);
			_player?.SetPlaybackSpeed(_speed);
		}

		private void EnsureStyles()
		{
			if (_titleStyle != null)
			{
				return;
			}

			_titleStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 20,
				fontStyle = FontStyle.Bold
			};
			_bodyStyle = new GUIStyle(GUI.skin.label)
			{
				wordWrap = true
			};
		}
	}
}
