namespace PersonDetector
{

	internal class Logger
	{
		private readonly string _filePath;
		private static readonly object _lock = new object();

		/// <summary>
		/// Constructor
		/// </summary>
		public Logger()
		{
			// Set the file path to the application's location
			_filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");

			// Check if the file exists, if not, create it
			if (!File.Exists(_filePath))
			{
				using (File.Create(_filePath)) { }
			}
		}

		/// <summary>
		/// Log message
		/// </summary>
		/// <param name="message"></param>
		/// <param name="name"></param>
		/// <param name="color"></param>
		public void Log(string message, string name, ConsoleColor color = ConsoleColor.Gray)
		{
			lock (_lock)
			{
				// Create the log message with the name and current time
				string logMessage = $"{DateTime.Now:yyyy:MM:dd HH:mm:ss} [{name}] {message}";

				// Log to console with specified color
				Console.ForegroundColor = color;
				Console.WriteLine(logMessage);
				Console.ResetColor();

				// Log to file
				using (StreamWriter writer = new StreamWriter(_filePath, true))
				{
					writer.WriteLine(logMessage);
				}
			}
		}
	}
}
