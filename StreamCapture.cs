using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Emgu.CV;
using Emgu.CV.Util;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon;
using MimeKit;

namespace PersonDetector
{
	internal class StreamCapture
	{
		/// <summary>
		/// Name of the stream
		/// </summary>
		public readonly string _name;
		/// <summary>
		/// RTSP stream URL
		/// </summary>
		private readonly string _stream;
		/// <summary>
		/// Bucket in AWS S3
		/// </summary>
		private readonly string _S3Bucket;
		/// <summary>
		/// Buffer Limit
		/// </summary>
		private readonly int _bufferLimit;
		/// <summary>
		/// Flag indicating if frames should be written
		/// </summary>
		private bool reading = false;
		/// <summary>
		/// Lock object for 'reading' variable
		/// </summary>
		private object _readingLock = new object();
		/// <summary>
		/// Flag indicating if the stream is running
		/// </summary>
		private bool running = false;
		/// <summary>
		/// List of tasks for processing
		/// </summary>
		private List<Task> tasks = new List<Task>();
		/// <summary>
		/// Buffer to hold frames
		/// </summary>
		private List<List<Frame>> buffer = new List<List<Frame>>();
		/// <summary>
		/// Lock object for synchronization
		/// </summary>
		private object _lock = new object();
		/// <summary>
		/// Object for logging
		/// </summary>
		Logger _logger;

		/// <summary>
		/// Constructor to initialize stream name and URL, and add tasks
		/// </summary>
		/// <param name="name"></param>
		/// <param name="stream"></param>
		/// <param name="logger"></param>
		public StreamCapture(string name, string stream, string S3Bucket, int bufferLimit ,Logger logger)
		{
			_name = name;
			_stream = stream;
			_S3Bucket = S3Bucket;
			_bufferLimit = bufferLimit;
			_logger = logger;
			// Task for frame capturing
			tasks.Add(new Task(FrameCatch));
			// Task for processing buffer
			tasks.Add(new Task(ProcessBufferItem));
		}

		/// <summary>
		/// Method to start the stream processing
		/// </summary>
		public StreamCapture Start()
		{
			// Set running to true
			running = true;
			// Start all tasks
			tasks.ForEach(t => t.Start());
			// Log start tasks
			_logger.Log($"Start tasks in {this._name} stream", this._name, ConsoleColor.Green);
			return this;
		}

		/// <summary>
		/// Method to stop the stream processing
		/// </summary>
		public async Task Stop()
		{
			// Set running to false
			running = false;			
			// Wait for all tasks to complete
			Task.WaitAll(tasks.ToArray());	
			// Log stop tasks
			_logger.Log($"Stop tasks in {this._name} stream", this._name, ConsoleColor.Red);
			// Process and clear the buffer
			ProcessBuffer();

			await Task.CompletedTask;
		}

		/// <summary>
		/// Method to capture frames from the RTSP stream
		/// </summary>
		private void FrameCatch()
		{
			while (running)
			{
				using (var capture = new VideoCapture(_stream, VideoCapture.API.Any))
				{
					this._logger.Log("create new 'capture'", this._name, ConsoleColor.Blue);
					List<Frame> internalList = new List<Frame>();
					
					while (running)
					{
						using (Mat frame = new Mat())
						{
							if (!capture.Read(frame))
							{
								_logger.Log("capture.Read : false", this._name, ConsoleColor.Red);
								// Sleep if frame capture fails
								Thread.Sleep(1000);
								break;
							}
							if(frame.IsEmpty)
							{
								_logger.Log("frame.IsEmpty", this._name, ConsoleColor.Red);
								Thread.Sleep(1000);
								break;
							}
							if (reading)
							{
								if (internalList.Count < this._bufferLimit) 
								{
									internalList.Add(new Frame() { date = $"{this._name}_{DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss")}", frame = frame.Clone() });
									//Thread.Sleep(1000);
								}
							}
							else
							{
								if (internalList.Count != 0)
								{
									lock (_lock)
									{
										// Add frames to buffer
										this.buffer.Add(internalList);
									}
									// Clear internal list
									internalList = new List<Frame>();
								}
							}
						}
					}
					// Dispose frames in internal list
					internalList.ForEach(f => f.frame.Dispose());
				}
			}
		}

		/// <summary>
		/// Method to process items in the buffer
		/// </summary>
		private async void ProcessBufferItem()
		{
			List<Frame> internalList = new List<Frame>();
			while (running)
			{
				lock (_lock)
				{
					if (buffer.Count > 0)
					{
						// add item from buffer to internal variable
						internalList = buffer.First();

						// Remove the item from the buffer
						buffer.RemoveAt(0);
					}
				}
				if (internalList.Count != 0)
				{
					// most higher confidence
					float finalConfidence = 0;

					// most higher confidence index
					int finalConfidenceIndex = 0;
					
					// final 'byte[]'
					Byte[] finalFrameToByte = null;

					for (int counter = 0; counter < internalList.Count; counter++)
					{
						// process first, second frame and frame which is in the middle of 'internalList'
						if (counter == 1 || counter == 2 || counter == 3 || counter == (internalList.Count / 2))
						{
							//convert 'frame' to 'byte[]'
							byte[] actualByte = ConvertMatToByteArray(internalList[counter].frame);
							//detect person and return confidence
							float actualConfidence = DetectPersonInImage(actualByte);
							//check if 'actualConfidence' is higher like 'finalConfidence'
							if (actualConfidence > finalConfidence)
							{
								finalConfidence = actualConfidence;
								finalConfidenceIndex = counter;
								finalFrameToByte = actualByte;
							}
						}
					}
					// check if some person was detected
					if(finalFrameToByte!= null)
					{
						// store image in AWS S3 bucket
						string imageKey =  await StoreToS3Bucket(finalFrameToByte, finalConfidence, finalConfidenceIndex);
						// create and send email
						this.SendEmailWithAttachment("t.kovacik@centrum.sk", "tomi.kovacik@gmail.com", $"{this._name}", $"Camera : {this._name}\nDatabase name : {imageKey}\nTime : {DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss")}", finalFrameToByte, "person.jpg", imageKey);
					}
					
					// Dispose all frames in the item
					foreach (var frame in internalList)
					{
						frame.frame.Dispose();
					}

					internalList.Clear();
				}
				// Add a short sleep to release CPU if the buffer is often empty
				Thread.Sleep(100);
			}
		}

		/// <summary>
		/// Send Email With Attachment
		/// </summary>
		/// <param name="from"></param>
		/// <param name="to"></param>
		/// <param name="subject"></param>
		/// <param name="body"></param>
		/// <param name="attachmentBytes"></param>
		/// <param name="attachmentName"></param>
		/// <param name="imageName"></param>
		private void SendEmailWithAttachment(string from, string to, string subject, string body, byte[] attachmentBytes, string attachmentName, string imageName)
		{
			using (var client = new AmazonSimpleEmailServiceClient(RegionEndpoint.EUCentral1)) // Adjust region as needed
			{
				var sendRequest = new SendRawEmailRequest
				{
					RawMessage = new RawMessage(CreateRawMessage(from, to, subject, body, attachmentBytes, attachmentName))
				};

				try
				{
					var response = client.SendRawEmailAsync(sendRequest).Result;
					this._logger.Log($"EMAIL : Send image \"{imageName}\" to : {to}", this._name);
				}
				catch (Exception ex)
				{
					this._logger.Log($"Failed to send email: {ex.Message}", this._name);
				}
			}
		}

		/// <summary>
		/// Create Raw Message
		/// </summary>
		/// <param name="from"></param>
		/// <param name="to"></param>
		/// <param name="subject"></param>
		/// <param name="body"></param>
		/// <param name="attachmentBytes"></param>
		/// <param name="attachmentName"></param>
		/// <returns></returns>
		private MemoryStream CreateRawMessage(string from, string to, string subject, string body, byte[] attachmentBytes, string attachmentName)
		{
			var message = new MimeKit.MimeMessage();
			var fromAddress = MailboxAddress.Parse(from);
			var toAddress = MailboxAddress.Parse(to);

			message.From.Add(fromAddress);
			message.To.Add(toAddress);
			message.Subject = subject;

			var bodyBuilder = new MimeKit.BodyBuilder
			{
				TextBody = body
			};

			using (var stream = new MemoryStream(attachmentBytes))
			{
				bodyBuilder.Attachments.Add(attachmentName, stream);
			}

			message.Body = bodyBuilder.ToMessageBody();

			var memoryStream = new MemoryStream();
			message.WriteTo(memoryStream);
			return memoryStream;
		}

		/// <summary>
		/// Method to process and clear the buffer
		/// </summary>
		private void ProcessBuffer()
		{
			lock (_lock)
			{
				foreach (var frameList in buffer)
				{
					foreach (var frame in frameList)
					{
						// Dispose frames in buffer
						frame.frame.Dispose();
					}
				}
				// Clear the buffer
				buffer.Clear();
			}
		}

		/// <summary>
		/// Send image to AWS Rekognito and evaluate result
		/// </summary>
		/// <param name="imageBytes"></param>
		/// <returns></returns>
		private float DetectPersonInImage(byte[] actualFrame)
		{
			// Inicializace AWS Rekognition klienta
			var rekognitionClient = new AmazonRekognitionClient();

			// Vytvoření požadavku DetectLabels
			var detectLabelsRequest = new DetectLabelsRequest
			{
				Image = new Amazon.Rekognition.Model.Image
				{
					Bytes = new MemoryStream(actualFrame)
				},
				MaxLabels = 10,
				MinConfidence = 75F
			};

			// Odeslání požadavku a získání odpovědi
			DetectLabelsResponse detectLabelsResponse = rekognitionClient.DetectLabelsAsync(detectLabelsRequest).Result;
			float internalConfidence = 0;
			// Kontrola přítomnosti osoby v detekovaných značkách
			foreach (var label in detectLabelsResponse.Labels)
			{
				if (label.Name.ToLower() == "person" && label.Confidence >= 75F)
				{
					// add actual confidence to internal variable
					if(internalConfidence < label.Confidence) internalConfidence = label.Confidence;
				}
			}

			return internalConfidence;
		}
		
		/// <summary>
		///  store image in AWS S3 bucket
		/// </summary>
		/// <param name="frameByte"></param>
		/// <returns></returns>
		private async Task<string> StoreToS3Bucket(byte[] frameByte, float finalConfidence, int finalConfidenceIndex)
		{
			string internalKey = $"{this._name.Replace("/", "_")}/{DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss")}_{finalConfidence.ToString()}_{finalConfidenceIndex.ToString()}";
			// Amazon S3 Client
			AmazonS3Client s3Client = new AmazonS3Client();

			using (var S3stream = new MemoryStream(frameByte))
			{
				var putRequest = new PutObjectRequest
				{
					BucketName = this._S3Bucket,
					Key = internalKey,
					InputStream = S3stream
				};
				//store image to aWS S3
				PutObjectResponse response = await s3Client.PutObjectAsync(putRequest);

				if (response.HttpStatusCode == System.Net.HttpStatusCode.OK) _logger.Log($"person with confidence : {finalConfidence} was store to AWS S3 {this._S3Bucket}/{this._name.Replace("/","_")}",this._name);

				return internalKey;
			}
		}

		/// <summary>
		/// COnvert Mat to ByteArray
		/// </summary>
		/// <param name="image"></param>
		/// <returns></returns>
		private byte[] ConvertMatToByteArray(Mat image)
		{
			VectorOfByte vector = new VectorOfByte();
			// convert 'Mat' to 'VectorOfByte'
			CvInvoke.Imencode(".jpg", image, vector);
			// return 'byte[]'
			return vector.ToArray();
		}
	
		/// <summary>
		/// Start reading frames from camera
		/// </summary>
		public void readingStart()
		{
			lock(_readingLock)
			{
				this.reading = true;
			}
			_logger.Log($"reading frames from stream {this._name} START on time : {DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss")}", this._name);
		}

		/// <summary>
		/// Stop reading frames from camera
		/// </summary>
		public void readingStop()
		{
			lock (_readingLock)
			{
				this.reading = false;
			}
			_logger.Log($"reading frames from stream {this._name} STOP on time : {DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss")}", this._name);
		}
	}
}
