using System.Reflection;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

namespace PersonDetector
{
	internal class Detector
	{
		private readonly Settings settings;

		public readonly string _name;

		IMqttClient mqttClient;

		Logger _logger;

		private List<StreamCapture> _streams;
		/// <summary>
		/// Constructor
		/// </summary>
		public Detector(Logger logger, string name)
		{
			this._name = name;
			this._logger = logger;
			this._streams = new List<StreamCapture>();
			try
			{
				// Get the current directory where the application is running (bin directory)
				string binPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

				// Assume that the settings.json file is 3 levels higher than the bin directory
				// The root project directory (where the .sln file is located) is usually 3 levels higher than bin\Debug\netX.X
				string projectRoot = Path.GetFullPath(Path.Combine(binPath, @"..\..\..\"));

				// Získanie cesty k settings.json
				string settingsFilePath = Path.Combine(projectRoot, "settings.json");

				// Get the path to settings.json
				string jsonString = File.ReadAllText(settingsFilePath); //"settings.json"
				settings = JsonSerializer.Deserialize<Settings>(jsonString);
				logger.Log("App settings was correctly uploaded", this._name, ConsoleColor.Green);

			}
			catch(Exception ex) 
			{
				// create empty 'Settings' object
				settings = new PersonDetector.Settings();
				logger.Log(ex.ToString(), this._name, ConsoleColor.Red);
			}

			//create mqtt client
			mqttClient = (new MqttFactory()).CreateMqttClient();
		}
		
		/// <summary>
		/// Settings
		/// </summary>
		/// <returns></returns>
		public async Task<Detector> Settings()
		{
			#region MQTT Connection
			// check if all parameters exist
			if (String.IsNullOrEmpty(settings.mqttServer) && 
					(settings.mqttPort == 0) && 
					String.IsNullOrEmpty(settings.mqttCredentialsName) && 
					String.IsNullOrEmpty(settings.mqttCredentialsPassword))
			{
				_logger.Log("MQTT Settings Missing", this._name, ConsoleColor.Red);
				return this;
			}
			// Event handlers
			this.mqttClient.ApplicationMessageReceivedAsync += HandleReceivedMessageAsync;
			this.mqttClient.ConnectedAsync += HandleConnectedAsync;
			this.mqttClient.DisconnectedAsync += HandleDisconnectedAsync;

			// Define MQTT connect options
			var mqttOptions = new MqttClientOptionsBuilder()
				.WithTcpServer(settings.mqttServer, settings.mqttPort)
				.WithCredentials(settings.mqttCredentialsName, settings.mqttCredentialsPassword)
				.Build();

			// try to connect to MQTT Broker
			MqttClientConnectResult result =  await this.mqttClient.ConnectAsync(mqttOptions);
			if (result.ResultCode != MqttClientConnectResultCode.Success)
			{
				_logger.Log($"MQTT Connect result : {result.ResultCode.ToString()}", this._name, ConsoleColor.Red);
				mqttClient.Dispose();
				return this;
			}

			_logger.Log($"MQTT Connect result : {result.ResultCode.ToString()}", this._name, ConsoleColor.Green);

			MqttFactory factory = new MqttFactory();
			// subscribe to topic in MQTT Broker
			_logger.Log($"Subscribing to topics...", this._name);
			foreach (var item in settings.streams)
			{
				// create subscribe option
				var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
				   .WithTopicFilter(f =>
				   {
					   f.WithTopic(item.Key)
						.WithAtMostOnceQoS();
				   })
				   .Build();
				// try subscribe to topic
				var response = await mqttClient.SubscribeAsync(subscribeOptions);

				// response from MQTT topic subscribing process
				foreach (var actualResult in response.Items)
				{
					if (actualResult.ResultCode == MqttClientSubscribeResultCode.GrantedQoS0 ||
						actualResult.ResultCode == MqttClientSubscribeResultCode.GrantedQoS1 ||
						actualResult.ResultCode == MqttClientSubscribeResultCode.GrantedQoS2)
					{
						_logger.Log($"Successfully subscribed to topic '{actualResult.TopicFilter.Topic}' with QoS {actualResult.ResultCode}.", this._name, ConsoleColor.Green);
					}
					else
					{
						_logger.Log($"Failed to subscribe to topic '{actualResult.TopicFilter.Topic}' with result code {actualResult.ResultCode}.", this._name, ConsoleColor.Red);
					}
				}
			}

			#endregion

			#region Create and start streams

			foreach (var item in settings.streams)
			{
				// Create new stream add it to '_streams' list and launch 'start' proces
				this._streams.Add((new StreamCapture(item.Key, item.Value, settings.S3Bucket.ToString(), settings.bufferLimit, _logger)).Start());
			}

			#endregion

			return this;
		}

		/// <summary>
		/// Send command to stop streams
		/// </summary>
		/// <returns></returns>
		public async Task<Task> Stop()
		{
			 this._streams.ForEach(async s => await s.Stop());
		
			 return Task.CompletedTask;
		}

		/// <summary>
		/// MQTT Message receive handler
		/// </summary>
		/// <param name="e"></param>
		/// <returns></returns>
		private async Task HandleReceivedMessageAsync(MqttApplicationMessageReceivedEventArgs e)
		{
			var message = e.ApplicationMessage;
			var payload = Encoding.UTF8.GetString(message.PayloadSegment);
			_logger.Log($"Received message: {payload} Topic: {message.Topic}", this._name);
			if (payload == "on") this._streams.ForEach(s => {
				if (s._name == message.Topic) s.readingStart();
			});
			else this._streams.ForEach(s => {
				if (s._name == message.Topic) s.readingStop();
			});

			// Async operation
			await Task.Yield();
		}

		/// <summary>
		/// MQTT Connected handler 
		/// </summary>
		/// <param name="e"></param>
		/// <returns></returns>
		private async Task HandleConnectedAsync(MqttClientConnectedEventArgs e)
		{
			_logger.Log("Connected successfully to MQTT Brokers.", this._name, ConsoleColor.Green);
			
			await Task.CompletedTask;
		}

		/// <summary>
		/// MQTT Disconnected handler 
		/// </summary>
		/// <param name="e"></param>
		/// <returns></returns>
		private async Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs e)
		{
			Console.WriteLine("Disconnected from MQTT Brokers.");
			if (e.ClientWasConnected)
			{
				// Reconnecting
				//Console.WriteLine("Reconnecting...");
				// Wait 5 second
				//await Task.Delay(TimeSpan.FromSeconds(5)); 
				//await ConnectToBrokerAsync(this.mqttClient, this.options);
			}
			await Task.CompletedTask;
		}

		// Reconnecting to broker
		//private async Task ConnectToBrokerAsync(IMqttClient mqttClient, IMqttClientOptions options)
		//{
		//	try
		//	{
		//		await mqttClient.ConnectAsync(options, CancellationToken.None);
		//		Console.WriteLine("The client is connected.");
		//	}
		//	catch (Exception ex)
		//	{
		//		Console.WriteLine($"An error occurred while connecting: {ex.Message}");
		//	}
		//}

		//"balcony/motionDetector": "rtsp://balcony:deX9hud6@192.168.50.215:554/stream1",

		
		//"garage/motionDetector": "rtsp://garage:deX9hud6@192.168.50.12:554/stream1"
	}
}
