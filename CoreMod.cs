using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using GorillaTag.Audio;
using HarmonyLib;
using Photon.Pun;
using Photon.Voice;
using Photon.Voice.Unity;
using Photon.Voice.Unity.UtilityScripts;
using POpusCodec.Enums;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClearMic
{
    #region plugin

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        void Awake()
        {
            new Harmony("com.xynz.clearmicremaster.patch").PatchAll();

            ToggleState.Init(Config);

            GameObject go = new GameObject("ClearMicRemaster");
            go.AddComponent<ClearMicBehaviour>();
            go.AddComponent<MicEchoToggle>();
            go.AddComponent<QualityToggle>();
            go.AddComponent<MicIndicator>();
            Object.DontDestroyOnLoad(go);

            Debug.Log("[ClearMic] Loaded.");
        }
    }
    public static class ToggleState
    {
        public static bool Muted { get => muted.Value; set => muted.Value = value; }
        public static bool PcMode { get => pcMode.Value; set => pcMode.Value = value; }
        public static bool IndicatorVisible { get => indicatorVisible.Value; set => indicatorVisible.Value = value; }
        public static bool EchoOn { get => echoOn.Value; set => echoOn.Value = value; }

        static ConfigEntry<bool> muted;
        static ConfigEntry<bool> pcMode;
        static ConfigEntry<bool> indicatorVisible;
        static ConfigEntry<bool> echoOn;

        public static void Init(ConfigFile config)
        {

            const string section = "Toggles";

            muted = config.Bind(section, "Muted", false,
                "Whether your mic starts muted when u start the game.");

            pcMode = config.Bind(section, "PcMode", false,
                "The quality mode of the mic. (using pc mode is not really reccommended but still, if u dont want questies to hear you, you can use this.)");

            indicatorVisible = config.Bind(section, "IndicatorVisible", true,
                "The mic indicator at the bottom right side of your screen.");

            echoOn = config.Bind(section, "EchoOn", false,
                "The mic feedback toggle that lets you hear yourself.");

        }
    }

    #endregion

    #region settings

    public static class Settings
    {
        public static float Gain = 30f;
        public static float Boost = 10f;
        public static float Ceiling = 0.725f;
        public static float KneeStart = 0f;
        public static float GateThreshold = 0f;
        public static float AttackMs = 0f;
        public static float ReleaseMs = 900f;

        public static SamplingRate SampleRate = SamplingRate.Sampling24000;
        public static int Bitrate = 30000;
    }

    #endregion

    #region quality toggle
    public class QualityToggle : MonoBehaviour
    {
        public static QualityToggle Instance;

        public bool PcMode => ToggleState.PcMode;

        bool onCooldown;

        void Awake()
        {
            Instance = this;

            Settings.SampleRate = ToggleState.PcMode ? SamplingRate.Sampling48000 : SamplingRate.Sampling24000;
            Settings.Bitrate = ToggleState.PcMode ? 96000 : 30000;
        }

        void Update()
        {
            if (Keyboard.current == null)
                return;

            if (!Keyboard.current.f7Key.wasPressedThisFrame)
                return;

            if (!Keyboard.current.ctrlKey.isPressed)
                return;

            if (onCooldown)
                return;

            Toggle();
        }

        void Toggle()
        {
            ToggleState.PcMode = !ToggleState.PcMode;

            Settings.SampleRate = PcMode ? SamplingRate.Sampling48000 : SamplingRate.Sampling24000;
            Settings.Bitrate = PcMode ? 96000 : 30000;

            if (MicIndicator.Instance != null)
                MicIndicator.Instance.PlayToggleSound();

            Debug.Log("[ClearMic] switched to " + (PcMode ? "pc mode" : "quest mode"));

            ReloadMic();

            if (MicIndicator.Instance != null)
                IndicatorState.NotifyQualityChanged();

            StartCoroutine(Cooldown());
        }

        IEnumerator Cooldown()
        {
            onCooldown = true;
            yield return new WaitForSeconds(0.5f);
            onCooldown = false;
        }

        void ReloadMic()
        {
            if (NetworkSystem.Instance == null || !NetworkSystem.Instance.InRoom)
                return;

            Recorder mic = NetworkSystem.Instance.VoiceConnection?.PrimaryRecorder;
            if (mic == null)
                return;

            MicSettings.Apply(mic);
        }
    }

    #endregion

    #region apply

    public class ClearMicBehaviour : MonoBehaviour
    {
        bool applied;

        void Update()
        {
            if (!PhotonNetwork.InRoom)
            {
                applied = false;
                return;
            }

            if (!applied)
            {
                applied = true;
                StartCoroutine(ApplyWhenReady());
            }
        }

        IEnumerator ApplyWhenReady()
        {
            Recorder mic = null;
            float timeout = 10f;
            float elapsed = 0f;

            while (mic == null && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;

                if (!PhotonNetwork.InRoom)
                {
                    applied = false;
                    yield break;
                }

                try
                {
                    if (NetworkSystem.Instance != null &&
                        NetworkSystem.Instance.VoiceConnection != null &&
                        NetworkSystem.Instance.VoiceConnection.PrimaryRecorder != null)
                        mic = NetworkSystem.Instance.VoiceConnection.PrimaryRecorder;
                }
                catch { }
            }

            if (mic == null)
            {
                Debug.LogWarning("[ClearMic] where mic at yo");
                applied = false;
                yield break;
            }

            MicSettings.Apply(mic);
        }
    }

    public static class MicSettings
    {
        // NOTE: not touching FrameDuration anymore, leave it at whatever the game has it at,
        // it was causing more problems than it was worth
        public static void Apply(Recorder mic)
        {
            mic.SamplingRate = Settings.SampleRate;
            mic.Bitrate = Settings.Bitrate;
            mic.VoiceDetection = false;
            mic.VoiceDetectionThreshold = 0.0f;

            try { mic.ReliableMode = true; } catch { }

            WebRtcAudioDsp dsp = mic.gameObject.GetComponent<WebRtcAudioDsp>();
            if (dsp != null)
            {
                dsp.NoiseSuppression = false;
                dsp.HighPass = false;
                dsp.AEC = false;
                dsp.enabled = false;
            }

            MicAmplifier stockAmp = mic.gameObject.GetComponent<MicAmplifier>();
            if (stockAmp != null)
                stockAmp.enabled = false;

            MicLimiterBehaviour limiter = mic.gameObject.GetComponent<MicLimiterBehaviour>();
            if (limiter == null)
                limiter = mic.gameObject.AddComponent<MicLimiterBehaviour>();

            limiter.Gain = Settings.Gain;
            limiter.Boost = Settings.Boost;
            limiter.Ceiling = Settings.Ceiling;
            limiter.KneeStart = Settings.KneeStart;
            limiter.GateThreshold = Settings.GateThreshold;
            limiter.AttackMs = Settings.AttackMs;
            limiter.ReleaseMs = Settings.ReleaseMs;
            limiter.enabled = true;

            // this loudness field is private for some reason so reflection it is
            GorillaSpeakerLoudness[] allGsl = GameObject.FindObjectsByType<GorillaSpeakerLoudness>(FindObjectsSortMode.None);
            foreach (GorillaSpeakerLoudness gsl in allGsl)
            {
                FieldInfo field = typeof(GorillaSpeakerLoudness).GetField("normalizedMax", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    field.SetValue(gsl, 10f);
            }

            Debug.Log("[ClearMic] applied settings, gain " + limiter.Gain + " ceiling " + limiter.Ceiling);

            if (!RecorderStartPatch.Ignore)
            {
                RecorderStartPatch.Ignore = true;
                mic.RestartRecording();
                RecorderStartPatch.Ignore = false;
            }
        }
    }

    [HarmonyPatch(typeof(Recorder), nameof(Recorder.StartRecording))]
    public class RecorderStartPatch
    {
        public static bool Ignore = false;

        static void Postfix(Recorder __instance)
        {
            if (Ignore) return;
            if (!PhotonNetwork.InRoom) return;
            if (NetworkSystem.Instance?.VoiceConnection?.PrimaryRecorder != __instance) return;

            MicSettings.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(Recorder), nameof(Recorder.IsCurrentlyTransmitting), MethodType.Getter)]
    public class TransmitPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!PhotonNetwork.InRoom) return true;
            __result = true;
            return false;
        }
    }

    public static class MuteControl
    {
        public static bool Muted => ToggleState.Muted;

        public static void Set(bool mute)
        {
            ToggleState.Muted = mute;

            if (NetworkSystem.Instance == null || !NetworkSystem.Instance.InRoom)
                return;

            Recorder mic = NetworkSystem.Instance.VoiceConnection?.PrimaryRecorder;
            if (mic == null)
                return;

            // actually stop the recorder instead of just lying about transmitting state,
            // that getter override alone wasnt actually muting anything
            mic.IsRecording = !mute;
        }

        public static void Toggle()
        {
            Set(!Muted);
        }
    }

    [HarmonyPatch(typeof(GorillaSpeakerLoudness), "UpdateLoudness")]
    public class LoudnessPatch
    {
        static bool Prefix(GorillaSpeakerLoudness __instance)
        {
            return __instance.gameObject.name != "Local Gorilla Player";
        }
    }

    #endregion

    #region echo


    public class MicEchoToggle : MonoBehaviour
    {
        bool appliedToCurrentRecorder;

        void Update()
        {

            Recorder mic = NetworkSystem.Instance?.VoiceConnection?.PrimaryRecorder;
            if (mic != null && mic.IsRecording && !appliedToCurrentRecorder)
            {
                mic.DebugEchoMode = ToggleState.EchoOn;
                appliedToCurrentRecorder = true;
            }
            else if (mic == null || !mic.IsRecording)
            {
                appliedToCurrentRecorder = false;
            }

            if (Keyboard.current == null)
                return;

            if (!Keyboard.current.f6Key.wasPressedThisFrame)
                return;

            if (!Keyboard.current.ctrlKey.isPressed)
                return;

            if (NetworkSystem.Instance == null || !NetworkSystem.Instance.InRoom)
                return;

            if (mic == null || !mic.IsRecording)
                return;

            bool echoOn = !mic.DebugEchoMode;
            mic.DebugEchoMode = echoOn;
            ToggleState.EchoOn = echoOn;

            if (MicIndicator.Instance != null)
                MicIndicator.Instance.PlayToggleSound();

            Debug.Log("[ClearMic] ear tumor " + (echoOn ? "on" : "off"));
        }
    }

    #endregion
}