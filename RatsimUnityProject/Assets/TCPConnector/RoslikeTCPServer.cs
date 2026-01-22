using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.XR;
using Newtonsoft.Json; // Add Newtonsoft.Json via Unity Package Manager or .dll



public class RoslikeTCPServer : MonoBehaviour
{
    public bool verbose = false; // Enable verbose logging
    public bool timingVerbose = false;
    public float physicsStepTime = 0.02f; // 50Hz

    static RoslikeTCPServer instance;
    public static RoslikeTCPServer GetInstance()
    {
        return instance;
    }

    private TcpListener listener;
    private Thread serverThread;
    private StreamWriter writer;

    // Change the subscribers dictionary to support different message types per topic
    private Dictionary<string, List<Action<Message>>> subscribersByTopic = new();
    private List<RoslikeTimer> timers = new List<RoslikeTimer>();

    private List<Tuple<string, Message>> receivedMessages = new List<Tuple<string, Message>>();

    List<Tuple<string, Message>> DeserializeMessages(Newtonsoft.Json.Linq.JArray rawMsgs)
    {
        var messages = new List<Tuple<string, Message>>();
        foreach (var rawMsg in rawMsgs)
        {
            // Deserialize outer wrapper
            var wrapper = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawMsg.ToString());
            string typeName = wrapper["type"].ToString();
            string topic = wrapper["topic"].ToString();
            var dataJson = wrapper["data"].ToString();

            if (verbose)
            {
                Debug.Log($"Received message of type {typeName} on topic {topic}");
            }

            //if (messageTypeRegistry.TryGetValue(typeName, out Type msgType))
                Type msgType = MessageRegistry.GetMessageType(typeName);
            if (msgType != null)
            {
                var fullJson = $"{{\"topic\":\"{topic}\",\"type\":\"{typeName}\",\"{nameof(dataJson)}\":{dataJson}}}";
                if (verbose)
                {
                    Debug.Log($"Deserializing message: {fullJson}");
                }

                var msg = (Message)JsonConvert.DeserializeObject(dataJson, msgType);

                messages.Add(new Tuple<string, Message>(topic, msg));
            }
            else
            {
                Debug.LogWarning($"Unknown message type: {typeName}");
            }
        }
        return messages;
    }

    void DispatchReceivedMessage(string topic, Message msg)
    {
        //Debug.Log("Dispatching message to topic: " + topic);
        //Debug.Log("Message type: " + msg.GetType().Name);
        //Debug.Log("Message data: " + JsonConvert.SerializeObject(msg));

        foreach (var subscriber in subscribersByTopic[topic])
        {
            if (subscriber is Action<Message> action)
            {
                action(msg);
            }
        }
    }

    public void RegisterTimerDiscrete( Action<TimerEvent> callback, uint stepsPerTick)
    {
        var timer = new RoslikeTimer(callback, true, stepsPerTick);
        timers.Add(timer);
    }

    public void RegisterTimerContinuous(Action<TimerEvent> callback, float periodSeconds)
    {
        var timer = new RoslikeTimer(callback, false, periodSeconds);
        timers.Add(timer);
    }

    void HandleTimers(uint elapsedPhysicsSteps, float elapsedSeconds)
    {
        foreach (var timer in timers)
        {
            timer.HandleSteps(elapsedPhysicsSteps, elapsedSeconds);
        }
    }

    public void Subscribe<T>(string topic, Action<T> callback) where T : Message
    {

        if (subscribersByTopic.TryGetValue(topic, out var subscribersOfThisTopic) == false)
        {
            subscribersOfThisTopic = new List<Action<Message>>();
            subscribersByTopic[topic] = subscribersOfThisTopic;
        }
        subscribersOfThisTopic.Add((Message msg) =>
        {
            if (msg is not T)
            {
                Debug.LogWarning($"Received message of type {msg.GetType().Name} but expected {typeof(T).Name}");
                return;
            }
            callback((T)msg);
        });
    }

    public void Publish(string topic, Message msg)
    {

        // Serialize the message
        MessageEnvelope wrapper = new MessageEnvelope
        (
            topic: topic,
            type: msg.GetType().Name,
            data: msg
        );

        // Add to envelopes to publish, since publishing is done synchronously in the main thread
        envelopesToPublish.Add(wrapper);
    }

    void SendAndClearEnvelopes()
    {
        string replyJson = JsonConvert.SerializeObject(new { messages = envelopesToPublish }) + "\n";
        writer.Write(replyJson);
        writer.Flush();
                                
        // Clear envelopes for next step
        envelopesToPublish.Clear();
    }

    void StepRequestCallback(StepRequestMessage msg)
    {
        if (msg.physicsEnabled != physicsEnabled)
        {
            Debug.LogWarning($"Received StepMessage with physicsEnabled={msg.physicsEnabled}, switching!");
        }
        physicsEnabled = msg.physicsEnabled;
    }

    List<MessageEnvelope> envelopesToPublish = new List<MessageEnvelope>();

    void Start()
    {
        if (instance != null)
        {
            Debug.LogWarning("Multiple instances of TcpServer detected, destroying this one.");
            Destroy(this);
            return;
        }
        instance = this;
        
        

        Physics.simulationMode = SimulationMode.Script;
        Application.targetFrameRate = 10000;
        serverThread = new Thread(MainLoop);
        serverThread.IsBackground = true;
        serverThread.Start();

    }


    public volatile bool stepRequested = false;
    public bool physicsEnabled = true;
    public uint stepIndex { get; private set; } = 0;
    public uint physicsStepIndex { get; private set; } = 0;


    void Update()
    {
        if (stepRequested)
        {
            // Handle Subscriber calbacks
            foreach (var msg in receivedMessages)
            {
                DispatchReceivedMessage(msg.Item1, msg.Item2);
            }

            // Step Unity physics
            if (physicsEnabled)
            {
                //Debug.Log("Stepping physics simulation...");
                physicsStepIndex++;
                Physics.Simulate(physicsStepTime);
            }

            // Handle Publishers and Timers
            HandleTimers(1, physicsStepTime);
            
            // Signify end of all mainthread operations
            stepRequested = false;
        }
    }

    void MainLoop()
    {
        listener = new TcpListener(IPAddress.Any, 9000);
        listener.Start();
        Debug.Log("TCP server started on port 9000");

        var client = listener.AcceptTcpClient();
        client.NoDelay = true;
        Debug.Log("Client connected");

        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };

        // Setup core subscribers
        Subscribe<StepRequestMessage>("/sim_control/do_step", StepRequestCallback);

        while (client.Connected)
        { 
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                //var mainloopstart = Time.realtimeSinceStartup;
                // Read incoming messages
                var line = reader.ReadLine();
                if (line == null) continue;
                var readingDoneTime = stopwatch.Elapsed.TotalSeconds;

                //Debug.Log("Received data len: " + line.Length);

                // try to parse the outer wrapper
                //var wrapper = JsonConvert.DeserializeObject<Dictionary<string, object>>(line);
                Dictionary<string, object> wrapper = null;
                try
                {
                    wrapper = JsonConvert.DeserializeObject<Dictionary<string, object>>(line);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Failed to parse message wrapper: " + e.Message);
                    continue;
                }

                var rawMsgs = wrapper["messages"] as Newtonsoft.Json.Linq.JArray;

                // Deserialize messages
                receivedMessages = DeserializeMessages(rawMsgs);

                var deserDoneTime = stopwatch.Elapsed.TotalSeconds;

                // Call Unity Update to handle Subs and Pubs in main thread
                stepIndex++;
                stepRequested = true;
                while (stepRequested)
                {
                    // Wait for the step to be processed by Update func calls
                }
                var updateDoneTime = stopwatch.Elapsed.TotalSeconds;


                // Add StepFinishedMessage to envelopes
                Publish("/sim_control/step_finished",
                    new StepFinishedMessage
                    {
                        success = true
                    });
                    /*envelopesToPublish.Add(new MessageEnvelope(
                        topic: "/sim_control/step_finished",
                        type: "StepFinishedMessage",
                        data: new StepFinishedMessage
                        {
                            success = true
                        }
                    ));*/

                // Serialize, send and clear envelopes
                SendAndClearEnvelopes();

                var sendingDoneTime = stopwatch.Elapsed.TotalSeconds;


                var mainloopend = stopwatch.Elapsed.TotalSeconds;
                if (timingVerbose)
                {
                    Debug.Log($"[Timing] Total main loop: {mainloopend:F4} seconds");
                    Debug.Log($"[Timing] Reading time: {readingDoneTime:F4} seconds");
                    Debug.Log($"[Timing] Deserialization time: {(deserDoneTime - readingDoneTime):F4} seconds");
                    Debug.Log($"[Timing] Update processing time: {(updateDoneTime - deserDoneTime):F4} seconds");
                    Debug.Log($"[Timing] Sending time: {(sendingDoneTime - updateDoneTime):F4} seconds");
                }
            }

          

            

            /*catch (Exception e)
            {
                Debug.LogError("Server loop error: " + e.Message);
                break;
            }*/
        }

        client.Close();
        listener.Stop();
    }
    
    void OnApplicationQuit()
    {
        listener?.Stop();
        serverThread?.Abort();
    }

}
