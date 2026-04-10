using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Mp2Unity.Networking
{
    [Serializable]
    public class PoseLandmark
    {
        public float x;
        public float y;
        public float z;
        public float visibility;
        public float presence;
    }

    [Serializable]
    public class PoseQuality
    {
        public float avg_visibility;
        public float avg_presence;
        public int point_count;
    }

    [Serializable]
    public class PosePacket
    {
        public string protocol;
        public int frame_id;
        public double timestamp;
        public string timestamp_iso;
        public bool world_landmarks_available;
        public PoseLandmark[] world_landmarks;
        public PoseQuality quality;
        public bool used_fallback;
        public float fps;
    }

    internal struct ReceivedEnvelope
    {
        public string Json;
        public int ByteCount;
        public double ReceivedEpochSeconds;
    }

    public class PoseUdpReceiver : MonoBehaviour
    {
        [Header("UDP")]
        [SerializeField] private int listenPort = 5005;
        [SerializeField] private string bindAddress = "0.0.0.0";
        [SerializeField] private bool autoStartOnEnable = true;

        [Header("Debug")]
        [SerializeField] private bool showOnScreenStats = true;
        [SerializeField] private bool logStatsToConsole = false;
        [SerializeField] private float reportIntervalSec = 1.0f;
        [SerializeField] private bool processLatestPacketOnly = true;

        private readonly ConcurrentQueue<ReceivedEnvelope> _queue = new ConcurrentQueue<ReceivedEnvelope>();
        private UdpClient _udpClient;
        private Thread _receiveThread;
        private volatile bool _running;

        private PosePacket _latestPacket;
        private bool _hasLatestPacket;
        private long _receivedPackets;
        private long _receivedBytes;
        private long _lostEstimate;
        private long _outOfOrderPackets;
        private long _badJsonPackets;
        private long _staleDroppedPackets;
        private bool _hasLastFrameId;
        private int _lastFrameId;
        private double _latencySumMs;
        private double _latencyMaxMs;
        private double _lastReceiveRealtime;
        private double _startedRealtime;
        private double _lastReportRealtime;
        private string _lastError = string.Empty;

        public PosePacket LatestPacket => _latestPacket;
        public bool HasLatestPacket => _hasLatestPacket;
        public long ReceivedPackets => _receivedPackets;
        public long LostEstimate => _lostEstimate;
        public long OutOfOrderPackets => _outOfOrderPackets;

        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                StartReceiver();
            }
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void OnApplicationQuit()
        {
            StopReceiver();
        }

        public void StartReceiver()
        {
            if (_running)
            {
                return;
            }

            ResetStats();

            IPAddress ip = IPAddress.Any;
            if (!string.IsNullOrWhiteSpace(bindAddress) && bindAddress != "0.0.0.0")
            {
                if (!IPAddress.TryParse(bindAddress, out ip))
                {
                    _lastError = $"绑定地址非法: {bindAddress}";
                    Debug.LogError($"[PoseUdpReceiver] {_lastError}");
                    return;
                }
            }

            try
            {
                _udpClient = new UdpClient(new IPEndPoint(ip, listenPort));
                _udpClient.Client.ReceiveTimeout = 500;
            }
            catch (Exception ex)
            {
                _lastError = $"UDP 绑定失败: {ex.Message}";
                Debug.LogError($"[PoseUdpReceiver] {_lastError}");
                return;
            }

            _running = true;
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "PoseUdpReceiverThread" };
            _receiveThread.Start();
            Debug.Log($"[PoseUdpReceiver] Started on udp://{ip}:{listenPort}");
        }

        public void StopReceiver()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            try
            {
                _udpClient?.Close();
            }
            catch
            {
                // Ignore close exceptions on shutdown.
            }

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(800);
            }

            _udpClient = null;
            _receiveThread = null;
            Debug.Log("[PoseUdpReceiver] Stopped.");
        }

        private void ResetStats()
        {
            _hasLatestPacket = false;
            _latestPacket = null;
            _receivedPackets = 0;
            _receivedBytes = 0;
            _lostEstimate = 0;
            _outOfOrderPackets = 0;
            _badJsonPackets = 0;
            _staleDroppedPackets = 0;
            _hasLastFrameId = false;
            _lastFrameId = -1;
            _latencySumMs = 0.0;
            _latencyMaxMs = 0.0;
            _lastReceiveRealtime = 0.0;
            _startedRealtime = Time.realtimeSinceStartupAsDouble;
            _lastReportRealtime = _startedRealtime;
            _lastError = string.Empty;

            while (_queue.TryDequeue(out _))
            {
                // Clear stale queue content before start.
            }
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    byte[] bytes = _udpClient.Receive(ref remote);
                    string json = Encoding.UTF8.GetString(bytes);
                    _queue.Enqueue(new ReceivedEnvelope
                    {
                        Json = json,
                        ByteCount = bytes.Length,
                        ReceivedEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
                    });
                }
                catch (SocketException ex)
                {
                    if (!_running)
                    {
                        break;
                    }

                    if (ex.SocketErrorCode == SocketError.TimedOut ||
                        ex.SocketErrorCode == SocketError.Interrupted)
                    {
                        continue;
                    }

                    _lastError = $"Socket异常: {ex.SocketErrorCode}";
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _lastError = $"接收线程异常: {ex.Message}";
                    Thread.Sleep(10);
                }
            }
        }

        private void Update()
        {
            if (processLatestPacketOnly)
            {
                bool hasLatest = false;
                ReceivedEnvelope latest = default;
                int dequeued = 0;
                while (_queue.TryDequeue(out ReceivedEnvelope envelope))
                {
                    latest = envelope;
                    hasLatest = true;
                    dequeued++;
                }

                if (dequeued > 1)
                {
                    _staleDroppedPackets += dequeued - 1;
                }

                if (hasLatest)
                {
                    ProcessEnvelope(latest);
                }
            }
            else
            {
                while (_queue.TryDequeue(out ReceivedEnvelope envelope))
                {
                    ProcessEnvelope(envelope);
                }
            }

            PrintPeriodicReport();
        }

        private void ProcessEnvelope(ReceivedEnvelope envelope)
        {
            PosePacket packet;
            try
            {
                packet = JsonUtility.FromJson<PosePacket>(envelope.Json);
            }
            catch
            {
                _badJsonPackets++;
                return;
            }

            if (packet == null || string.IsNullOrEmpty(packet.protocol))
            {
                _badJsonPackets++;
                return;
            }

            _latestPacket = packet;
            _hasLatestPacket = true;
            _receivedPackets++;
            _receivedBytes += envelope.ByteCount;
            _lastReceiveRealtime = Time.realtimeSinceStartupAsDouble;

            if (_hasLastFrameId)
            {
                if (packet.frame_id > _lastFrameId + 1)
                {
                    _lostEstimate += packet.frame_id - _lastFrameId - 1;
                }
                else if (packet.frame_id <= _lastFrameId)
                {
                    _outOfOrderPackets++;
                }
            }

            _lastFrameId = packet.frame_id;
            _hasLastFrameId = true;

            if (packet.timestamp > 0.0)
            {
                double latencyMs = Math.Max(0.0, (envelope.ReceivedEpochSeconds - packet.timestamp) * 1000.0);
                _latencySumMs += latencyMs;
                if (latencyMs > _latencyMaxMs)
                {
                    _latencyMaxMs = latencyMs;
                }
            }
        }

        private void PrintPeriodicReport()
        {
            if (!logStatsToConsole)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastReportRealtime < Math.Max(0.1f, reportIntervalSec))
            {
                return;
            }

            Debug.Log($"[PoseUdpReceiver] {BuildStatusText(singleLine: true)}");
            _lastReportRealtime = now;
        }

        private string BuildStatusText(bool singleLine)
        {
            long expected = _receivedPackets + _lostEstimate;
            double success = expected > 0 ? (_receivedPackets * 100.0 / expected) : 100.0;
            double avgLatency = _receivedPackets > 0 ? _latencySumMs / _receivedPackets : 0.0;
            double uptime = Math.Max(0.0, Time.realtimeSinceStartupAsDouble - _startedRealtime);
            double pps = uptime > 1e-6 ? _receivedPackets / uptime : 0.0;
            double kbps = uptime > 1e-6 ? (_receivedBytes * 8.0 / 1000.0) / uptime : 0.0;
            double idleMs = _lastReceiveRealtime > 0.0
                ? Math.Max(0.0, (Time.realtimeSinceStartupAsDouble - _lastReceiveRealtime) * 1000.0)
                : -1.0;
            string noData = _hasLatestPacket ? "" : " (waiting first packet)";
            string sep = singleLine ? " | " : "\n";

            return
                $"running={_running}{noData}{sep}" +
                $"frame={(_hasLastFrameId ? _lastFrameId.ToString() : "-")}{sep}" +
                $"recv={_receivedPackets} pps={pps:F1} kbps={kbps:F1}{sep}" +
                $"success={success:F2}% lost={_lostEstimate} ooo={_outOfOrderPackets} bad={_badJsonPackets} stale_drop={_staleDroppedPackets}{sep}" +
                $"latency_avg={avgLatency:F1}ms latency_max={_latencyMaxMs:F1}ms idle_ms={(idleMs >= 0 ? idleMs.ToString("F1") : "-")}{sep}" +
                $"last_error={(_lastError.Length > 0 ? _lastError : "none")}";
        }

        private void OnGUI()
        {
            if (!showOnScreenStats)
            {
                return;
            }
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            const float width = 620f;
            const float height = 120f;
            GUI.Box(new Rect(12f, 12f, width, height), BuildStatusText(singleLine: false));
        }
    }
}
