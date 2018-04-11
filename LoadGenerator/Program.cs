using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;

namespace LoadGenerator
{
    class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                CommandLineOptionsClass commandLineOptions = new CommandLineOptionsClass();
                var isValid = CommandLine.Parser.Default.ParseArgumentsStrict(args, commandLineOptions);

                var app = new Program();
                Task.WaitAll(app.MainAsync(commandLineOptions));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }
            Console.WriteLine("Execution Completed");
            return 0;
        }

		private DateTime started;
		private DateTime ended;

        public async Task MainAsync(CommandLineOptionsClass commandLineOptions)
        {
            List<Task> tasks = new List<Task>();
			started = DateTime.Now;
            for (int thread = 0; thread < commandLineOptions.Threads; thread++)
            {
                Task t = Task.Run(async () => {
                    await GenerateLoad(commandLineOptions);
                });
                tasks.Add(t);
            }
            Task.WaitAll(tasks.ToArray());
			ended = DateTime.Now;
			PrintFinalStatement(commandLineOptions);
        }

		private void PrintFinalStatement(CommandLineOptionsClass commandLineOptions)
		{
			var timeSpent = ended - started;
			Console.WriteLine(new string('-', 30));
			Console.WriteLine("Sent total messages:         {0}", commandLineOptions.MessagesToSend);
			Console.WriteLine("Thread count:                {0}", commandLineOptions.Threads);
			Console.WriteLine("Message size:                {0}", commandLineOptions.MessageSize);
			Console.WriteLine("Batch size:                  {0}", commandLineOptions.BatchSize);
			Console.WriteLine("Messages/sec:                {0}", (commandLineOptions.MessagesToSend / timeSpent.TotalSeconds).ToString("N2"));
			Console.WriteLine("Time spent:                  {0}m", timeSpent.ToString(@"hh\:mm\:ss"));
		}

		private async Task GenerateLoad(CommandLineOptionsClass commandLineOptions)
        {
            string utcTimeStamp;
            string randomPayload;
            string payload;
            DateTime start;
            Int64 messageNumber = 0;
            BrokeredMessage message;
            List<BrokeredMessage> messageBatch = new List<BrokeredMessage>();
			
            QueueClient sendClient = QueueClient.CreateFromConnectionString(commandLineOptions.ConnectionString, commandLineOptions.EHOrQueueOrTopicName);
			
            Console.WriteLine($"Thread: {Thread.CurrentThread.ManagedThreadId}, started and connected");
			bool exhappened = false;
            try
            {
                start = DateTime.Now;
                while (messageNumber < commandLineOptions.MessagesToSend || commandLineOptions.MessagesToSend <= 0)
                {
                    utcTimeStamp = ((long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds).ToString();
                    randomPayload = new Bogus.Randomizer().ClampString("", commandLineOptions.MessageSize, commandLineOptions.MessageSize);
                    payload = String.Format("{{\"dt\":{0},\"payload\":\"{1}\"}}", utcTimeStamp, randomPayload);
                    message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(payload)))
                    {
                        ContentType = "application/json",
                        Label = "MyPayload",
                        TimeToLive = TimeSpan.FromMinutes(100)
                    };
                    if(!commandLineOptions.BatchMode)
                    {
                        await sendClient.SendAsync(message);
                        if (messageNumber % commandLineOptions.Checkpoint == 0 && messageNumber > 0)
                        {
                            Console.WriteLine($"Thread: {Thread.CurrentThread.ManagedThreadId}, sent: {messageNumber} / {commandLineOptions.MessagesToSend} messages, message size: {message.Size} bytes, speed: {messageNumber / (DateTime.Now - start).TotalSeconds} msg/sec");
                        }
                    }
                    else
                    {
                        messageBatch.Add(message);
                        if((messageNumber % commandLineOptions.BatchSize == 0 && messageNumber > 0) || 
                            (messageNumber == (commandLineOptions.MessagesToSend - 1)))
                        {
                            await sendClient.SendBatchAsync(messageBatch);
                            Console.WriteLine($"Thread: {Thread.CurrentThread.ManagedThreadId}, sent: {messageNumber} / {commandLineOptions.MessagesToSend} messages total, in batches of {commandLineOptions.BatchSize}, message size: {message.Size} bytes, speed: {messageNumber / (DateTime.Now - start).TotalSeconds} msg/sec");
                            messageBatch.Clear();
                        }
                    }
                    messageNumber++;
                }
            }
            catch (Exception ex)
            {
				if (exhappened) return;
				//ignore, keep bombarding!
				Console.WriteLine(ex.ToString());
				exhappened = true;
            }
            finally
            {
                await sendClient.CloseAsync();
                Console.WriteLine($"Thread: {Thread.CurrentThread.ManagedThreadId}, finished");
            }
        }
    }


}
