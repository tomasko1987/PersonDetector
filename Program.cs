using System;
using System.Drawing;
using PersonDetector;
using System.Threading.Tasks;

class Program
{
	static async Task Main(string[] args)
	{
		Logger logger = new Logger();
		Detector detector = await (new Detector(logger, "Detector")).Settings();
		Console.ReadKey();
		logger.Log("Start ending process","Main", ConsoleColor.Red);
		await detector.Stop();
	 }
}

