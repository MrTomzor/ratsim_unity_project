using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using NetMQ;
using NetMQ.Sockets;

public class ZmqUnityServer : MonoBehaviour
{
    public int port = 9000;
    public bool verbose = false;
    public bool timingVerbose = false;
    public float physicsStepTime = 0.02f; // 50Hz

    private static ZmqUnityServer instance;
    public static ZmqUnityServer GetInstance()
    {
        if (instance == null)
        {
            instance = FindObjectOfType<ZmqUnityServer>();
        }
        return instance;
    }

    // SCENE HANDLING
    private string currentLoadedScene = null;

    void OnSceneSelectReceived(StringMessage msg)
    {
        string sceneName = msg.data;
        if (verbose)
        {
            Debug.Log($"[ZmqUnityServer] Received scene load request: {sceneName}");
        }

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            currentLoadedScene = sceneName;
            Debug.Log($"[ZmqUnityServer] Loading scene: {sceneName}");
        }
        else
        {
            Debug.LogWarning($"[ZmqUnityServer] Scene '{sceneName}' not found in build settings!");
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CleanupDestroyedTimersAndSubscribers();
        Debug.Log($"[ZmqUnityServer] Scene loaded: {scene.name}, cleaned up timers/subscribers");
    }

    // SUBSCRIBERS AND TIMERS
    private Dictionary<string, List<ZmqSubscriber>> subscribersByTopic = new();
    private List<ZmqTimer> timers = new List<ZmqTimer>();
    private List<Tuple<string, Message>> receivedMessages = new List<Tuple<string, Message>>();
    private List<ZmqMessageEnvelope> envelopesToPublish = new List<ZmqMessageEnvelope>();
    private List<byte[]> binaryAttachmentsToPublish = new List<byte[]>();

    public volatile bool stepRequested = false;
    public bool physicsEnabled = true;
    public uint stepIndex { get; private set; } = 0;
    public uint physicsStepIndex { get; private set; } = 0;

    // THREADING & NETMQ
    private Thread serverThread;
    private volatile bool isRunning = false;
    private ResponseSocket serverSocket;

    void Awake()
    {
        AsyncIO.ForceDotNet.Force();

        if (instance != null && instance != this)
        {
            Debug.LogWarning("[ZmqUnityServer] Multiple instances detected, destroying this one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        currentLoadedScene = SceneManager.GetActiveScene().name;
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Core subscribers
        Subscribe<StringMessage>("/sim_control/scene_select", OnSceneSelectReceived);
        Subscribe<StepRequestMessage>("/sim_control/do_step", StepRequestCallback);
    }

    void Start()
    {
        Physics.autoSyncTransforms = false;
        Time.fixedDeltaTime = physicsStepTime;
        Time.maximumDeltaTime = physicsStepTime;
        Physics.simulationMode = SimulationMode.Script;
        Application.targetFrameRate = 10000;

        // Start NetMQ background thread
        isRunning = true;
        serverThread = new Thread(ServerLoop);
        serverThread.IsBackground = true;
        serverThread.Start();
        Debug.Log($"[ZmqUnityServer] Started on tcp://0.0.0.0:{port}");
    }

    void Update()
    {
        if (stepRequested)
        {
            // Handle Subscriber callbacks
            foreach (var msg in receivedMessages)
            {
                DispatchReceivedMessage(msg.Item1, msg.Item2);
            }

            // Step Unity physics
            if (physicsEnabled)
            {
                physicsStepIndex++;
                Physics.Simulate(physicsStepTime);
            }

            // Handle Timers
            HandleTimers(1, physicsStepTime);

            // Signal completion to background thread
            stepRequested = false;
        }
    }

    private void ServerLoop()
    {
        try
        {
            serverSocket = new ResponseSocket();
            serverSocket.Options.Linger = TimeSpan.Zero;
            serverSocket.Bind($"tcp://0.0.0.0:{port}");

            while (isRunning)
            {
                // Poll with timeout so thread can exit cleanly
                if (!serverSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(100), out string requestJson))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(requestJson))
                {
                    continue;
                }

                // Fast handshake response without stepping physics
                if (requestJson.Contains("/sim_control/ping"))
                {
                    serverSocket.SendFrame("{\"messages\":[{\"topic\":\"/sim_control/pong\",\"type\":\"StringMessage\",\"data\":{\"data\":\"pong\"}}]}\n");
                    continue;
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Parse request wrapper
                Dictionary<string, object> wrapper = null;
                try
                {
                    wrapper = JsonConvert.DeserializeObject<Dictionary<string, object>>(requestJson);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ZmqUnityServer] Failed to parse message wrapper: {e.Message}");
                    continue;
                }

                if (wrapper != null && wrapper.ContainsKey("messages") && wrapper["messages"] is Newtonsoft.Json.Linq.JArray rawMsgs)
                {
                    receivedMessages = DeserializeMessages(rawMsgs);
                }
                else
                {
                    receivedMessages = new List<Tuple<string, Message>>();
                }

                var deserDoneTime = stopwatch.Elapsed.TotalSeconds;

                // Trigger Unity Update()
                stepIndex++;
                stepRequested = true;

                while (stepRequested && isRunning)
                {
                    Thread.Sleep(0);
                }

                var updateDoneTime = stopwatch.Elapsed.TotalSeconds;

                // Add step finished notification
                Publish("/sim_control/step_finished", new StepFinishedMessage { success = true });

                // Send response (multipart if binary attachments exist)
                string replyJson = JsonConvert.SerializeObject(new { messages = envelopesToPublish }) + "\n";
                if (binaryAttachmentsToPublish.Count > 0)
                {
                    serverSocket.SendMoreFrame(replyJson);
                    for (int i = 0; i < binaryAttachmentsToPublish.Count; i++)
                    {
                        if (i == binaryAttachmentsToPublish.Count - 1)
                        {
                            serverSocket.SendFrame(binaryAttachmentsToPublish[i]);
                        }
                        else
                        {
                            serverSocket.SendMoreFrame(binaryAttachmentsToPublish[i]);
                        }
                    }
                    binaryAttachmentsToPublish.Clear();
                }
                else
                {
                    serverSocket.SendFrame(replyJson);
                }
                envelopesToPublish.Clear();

                var sendingDoneTime = stopwatch.Elapsed.TotalSeconds;

                if (timingVerbose)
                {
                    Debug.Log($"[ZmqTiming] Deser: {deserDoneTime:F4}s | Update: {(updateDoneTime - deserDoneTime):F4}s | Send: {(sendingDoneTime - updateDoneTime):F4}s");
                }
            }
        }
        catch (ThreadAbortException)
        {
            // Normal when thread terminates
        }
        catch (ObjectDisposedException)
        {
            // Expected when socket is closed on shutdown
        }
        catch (Exception e)
        {
            if (isRunning)
            {
                Debug.LogError($"[ZmqUnityServer] Exception in server loop: {e}");
            }
        }
        finally
        {
            try
            {
                serverSocket?.Close();
                serverSocket?.Dispose();
                serverSocket = null;
            }
            catch (Exception) {}
        }
    }

    List<Tuple<string, Message>> DeserializeMessages(Newtonsoft.Json.Linq.JArray rawMsgs)
    {
        var messages = new List<Tuple<string, Message>>();
        foreach (var rawMsg in rawMsgs)
        {
            var wrapper = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawMsg.ToString());
            string typeName = wrapper["type"].ToString();
            string topic = wrapper["topic"].ToString();
            var dataJson = wrapper["data"].ToString();

            if (verbose)
            {
                Debug.Log($"[ZmqUnityServer] Received message of type {typeName} on topic {topic}");
            }

            Type msgType = ZmqMessageRegistry.GetMessageType(typeName);
            if (msgType != null)
            {
                try
                {
                    var msg = (Message)JsonConvert.DeserializeObject(dataJson, msgType);
                    messages.Add(new Tuple<string, Message>(topic, msg));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ZmqUnityServer] Failed to deserialize message {typeName} on {topic}: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"[ZmqUnityServer] Unknown message type: {typeName}");
            }
        }
        return messages;
    }

    void DispatchReceivedMessage(string topic, Message msg)
    {
        if (!subscribersByTopic.ContainsKey(topic))
        {
            return;
        }

        foreach (var subscriber in subscribersByTopic[topic])
        {
            subscriber.callback(msg);
        }
    }

    public void Subscribe<T>(string topic, Action<T> callback) where T : Message
    {
        if (!subscribersByTopic.TryGetValue(topic, out var list))
        {
            list = new List<ZmqSubscriber>();
            subscribersByTopic[topic] = list;
        }

        UnityEngine.Object owner = callback.Target as UnityEngine.Object;

        list.Add(new ZmqSubscriber
        {
            owner = owner,
            callback = (Message msg) =>
            {
                if (msg is T typed)
                    callback(typed);
            }
        });
    }

    public void Publish(string topic, Message msg)
    {
        var envelope = new ZmqMessageEnvelope(
            topic: topic,
            type: msg.GetType().Name,
            data: msg
        );
        envelopesToPublish.Add(envelope);
    }

    public void PublishBinary(string topic, Message msg, byte[] binaryData)
    {
        int index = binaryAttachmentsToPublish.Count + 1; // 1-indexed (0 is JSON envelope)
        if (msg is RawImageMessage imgMsg)
        {
            imgMsg.binaryIndex = index;
        }
        else if (msg is RawLidarMessage lidarMsg)
        {
            lidarMsg.rangesBinaryIndex = index;
        }
        Publish(topic, msg);
        binaryAttachmentsToPublish.Add(binaryData);
    }

    public void PublishBinaryDual(string topic, Message msg, byte[] primaryBinary, byte[] secondaryBinary)
    {
        int idx1 = binaryAttachmentsToPublish.Count + 1;
        int idx2 = idx1 + 1;
        if (msg is RawImageMessage imgMsg)
        {
            imgMsg.binaryIndex = idx1;
            imgMsg.depthBinaryIndex = idx2;
        }
        else if (msg is RawLidarMessage lidarMsg)
        {
            lidarMsg.rangesBinaryIndex = idx1;
            lidarMsg.descriptorsBinaryIndex = idx2;
        }
        Publish(topic, msg);
        binaryAttachmentsToPublish.Add(primaryBinary);
        binaryAttachmentsToPublish.Add(secondaryBinary);
    }

    public void RegisterTimerDiscrete(Action<ZmqTimerEvent> callback, uint stepsPerTick)
    {
        var timer = new ZmqTimer(callback, true, stepsPerTick);
        timers.Add(timer);
    }

    public void RegisterTimerContinuous(Action<ZmqTimerEvent> callback, float periodSeconds)
    {
        var timer = new ZmqTimer(callback, false, periodSeconds);
        timers.Add(timer);
    }

    void HandleTimers(uint elapsedPhysicsSteps, float elapsedSeconds)
    {
        foreach (var timer in timers)
        {
            timer.HandleSteps(elapsedPhysicsSteps, elapsedSeconds);
        }
    }

    public void CleanupDestroyedTimersAndSubscribers()
    {
        timers.RemoveAll(timer =>
        {
            if (timer.callback == null) return true;
            if (timer.callback.Target is UnityEngine.Object unityObj)
            {
                return unityObj == null;
            }
            return false;
        });

        foreach (var topic in subscribersByTopic.Keys.ToList())
        {
            var subs = subscribersByTopic[topic];
            subs.RemoveAll(sub =>
            {
                if (sub == null) return true;
                if (sub.owner is UnityEngine.Object unityObj)
                {
                    return unityObj == null;
                }
                return false;
            });

            if (subs.Count == 0)
            {
                subscribersByTopic.Remove(topic);
            }
        }
    }

    void StepRequestCallback(StepRequestMessage msg)
    {
        if (msg.physicsEnabled != physicsEnabled)
        {
            Debug.LogWarning($"[ZmqUnityServer] Received StepMessage with physicsEnabled={msg.physicsEnabled}, switching!");
        }
        physicsEnabled = msg.physicsEnabled;
    }

    private void StopServer()
    {
        isRunning = false;
        if (serverThread != null && serverThread.IsAlive)
        {
            serverThread.Join(500);
        }
        try
        {
            serverSocket?.Close();
            serverSocket?.Dispose();
            serverSocket = null;
        }
        catch (Exception) {}
        try
        {
            NetMQConfig.Cleanup(false);
        }
        catch (Exception)
        {
            // Ignore during domain teardown
        }
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            StopServer();
        }
    }

    void OnApplicationQuit()
    {
        if (instance == this)
        {
            StopServer();
        }
    }
}
