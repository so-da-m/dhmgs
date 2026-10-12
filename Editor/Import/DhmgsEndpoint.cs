namespace Mnnh.Dhmgs.Editor
{
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
