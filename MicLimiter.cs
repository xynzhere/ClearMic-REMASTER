using System;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

namespace ClearMic
{
    #region processor

    public class MicLimiterFloat : IProcessor<float>, IDisposable
    {
        public float Gain { get; set; }
        public float Boost { get; set; }
        public float Ceiling { get; set; } = 0.891f;
        public float KneeStart { get; set; } = 0.7f;
        public float GateThreshold { get; set; } = 0.02f;
        public int GateFadeSamples { get; set; } = 480;
        public float AttackMs { get; set; } = 5f;
        public float ReleaseMs { get; set; } = 80f;
        public int SampleRate { get; set; } = 48000;

        public bool Disabled { get; set; }

        public float CurrentLevel { get; private set; }

        float envGain = 1f;
        float gateEnv;

        public MicLimiterFloat(float gain, float boost)
        {
            Gain = gain;
            Boost = boost;
        }

        public float[] Process(float[] buf)
        {
            if (Disabled)
                return buf;

            float knee = Ceiling * KneeStart;
            float kneeRange = Ceiling - knee;

            float attackCoeff = TimeConstantToCoeff(AttackMs, SampleRate);
            float releaseCoeff = TimeConstantToCoeff(ReleaseMs, SampleRate);

            float gateStep = GateFadeSamples > 0 ? 1f / GateFadeSamples : 1f;

            float peak = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float rawAbs = Math.Abs(buf[i]);
                if (rawAbs > peak) peak = rawAbs;

                float gained = buf[i] * Gain + Boost;
                float absGained = Math.Abs(gained);

                bool aboveThreshold = absGained >= GateThreshold;
                gateEnv = aboveThreshold ? Math.Min(1f, gateEnv + gateStep) : Math.Max(0f, gateEnv - gateStep);

                float gated = gained * gateEnv;
                float absGated = Math.Abs(gated);

                float targetGain = 1f;
                if (absGated > knee && kneeRange > 0.0001f)
                {
                    float over = (absGated - knee) / kneeRange;
                    float curved = (float)Math.Tanh(over);
                    float desiredMag = knee + curved * kneeRange;
                    targetGain = desiredMag / absGated;
                }

                float coeff = targetGain < envGain ? attackCoeff : releaseCoeff;
                envGain = targetGain + (envGain - targetGain) * coeff;

                buf[i] = gated * envGain;
            }

            CurrentLevel = CurrentLevel + (Mathf.Clamp01(peak) - CurrentLevel) * 0.5f;

            return buf;
        }

        static float TimeConstantToCoeff(float ms, int sr)
        {
            if (ms <= 0f) return 0f;
            return (float)Math.Exp(-1.0 / (ms / 1000f * sr));
        }

        public void Dispose() { }
    }

    #endregion

    #region behaviour

    [RequireComponent(typeof(Recorder))]
    public class MicLimiterBehaviour : VoiceComponent
    {
        public float Gain { get => gain; set { gain = value; if (proc != null) proc.Gain = value; } }
        public float Boost { get => boost; set { boost = value; if (proc != null) proc.Boost = value; } }
        public float Ceiling { get => ceiling; set { ceiling = value; if (proc != null) proc.Ceiling = value; } }
        public float KneeStart { get => kneeStart; set { kneeStart = value; if (proc != null) proc.KneeStart = value; } }
        public float GateThreshold { get => gateThreshold; set { gateThreshold = value; if (proc != null) proc.GateThreshold = value; } }
        public float AttackMs { get => attackMs; set { attackMs = value; if (proc != null) proc.AttackMs = value; } }
        public float ReleaseMs { get => releaseMs; set { releaseMs = value; if (proc != null) proc.ReleaseMs = value; } }

        public float CurrentLevel => proc?.CurrentLevel ?? 0f;

        [SerializeField] float gain = 6f;
        [SerializeField] float boost;
        [SerializeField] float ceiling = 0.891f;
        [SerializeField] float kneeStart = 0.7f;
        [SerializeField] float gateThreshold = 0.02f;
        [SerializeField] float attackMs = 5f;
        [SerializeField] float releaseMs = 80f;

        MicLimiterFloat proc;

        void OnEnable()
        {
            if (proc != null) proc.Disabled = false;
        }

        void OnDisable()
        {
            if (proc != null) proc.Disabled = true;
        }

        void PhotonVoiceRemoved()
        {
            proc = null;
        }

        void PhotonVoiceCreated(PhotonVoiceCreatedParams p)
        {
            if (proc != null)
                return;

            LocalVoiceAudioFloat lv = p.Voice as LocalVoiceAudioFloat;
            if (lv == null)
                return;

            proc = new MicLimiterFloat(Gain, Boost)
            {
                Ceiling = Ceiling,
                KneeStart = KneeStart,
                GateThreshold = GateThreshold,
                AttackMs = AttackMs,
                ReleaseMs = ReleaseMs,
                SampleRate = lv.Info.SamplingRate > 0 ? lv.Info.SamplingRate : 48000,
                Disabled = !enabled
            };

            lv.AddPostProcessor(new IProcessor<float>[] { proc });
        }
    }

    #endregion
}