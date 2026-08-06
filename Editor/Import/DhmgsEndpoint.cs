namespace Mnnh.Dhmgs.Editor
{
	/// <summary>ExAvatar 서버 URL 조립. 단일 엔드포인트: GET {base}/{attempt_id} → zip.</summary>
	internal static class DhmgsEndpoint
	{
		public static string Normalize(string baseUrl)
		{
			return (baseUrl ?? string.Empty).TrimEnd('/');
		}

		public static string Attempt(string baseUrl, int attemptId)
		{
			return $"{Normalize(baseUrl)}/{attemptId}";
		}
	}
}
