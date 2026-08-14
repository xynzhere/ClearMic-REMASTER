using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace ClearMic
{
    public static class IndicatorState
    {
        public static bool Visible
        {
            get => ToggleState.IndicatorVisible;
            set => ToggleState.IndicatorVisible = value;
        }
        public static float DisplayLevel;

        public static bool Flashing;
        public static float FlashTimer;
        public const float FlashOnTime = 0.15f;
        public const float FlashOffTime = 0.15f;
        public const int FlashCount = 3;
        public const float HoldAfterFlash = 1.0f;
        public const float FadeAfterHold = 0.6f;

        public static void NotifyQualityChanged()
        {
            Flashing = true;
            FlashTimer = 0f;
        }

        public static float GetDotAlpha()
        {
            if (!Flashing)
                return 0f;

            float cycle = FlashOnTime + FlashOffTime;
            float flashPhaseEnd = cycle * FlashCount;

            if (FlashTimer < flashPhaseEnd)
            {
                float t = FlashTimer % cycle;
                return t < FlashOnTime ? 1f : 0f;
            }

            float holdEnd = flashPhaseEnd + HoldAfterFlash;
            if (FlashTimer < holdEnd)
                return 1f;

            float fadeEnd = holdEnd + FadeAfterHold;
            if (FlashTimer < fadeEnd)
                return 1f - (FlashTimer - holdEnd) / FadeAfterHold;

            Flashing = false;
            return 0f;
        }

        public static void Tick()
        {
            if (Keyboard.current == null)
                return;

            if (Keyboard.current.f8Key.wasPressedThisFrame && Keyboard.current.ctrlKey.isPressed)
                Visible = !Visible;

            if (Keyboard.current.f5Key.wasPressedThisFrame && Keyboard.current.ctrlKey.isPressed)
            {
                MuteControl.Toggle();
                if (MicIndicator.Instance != null)
                    MicIndicator.Instance.PlayToggleSound();
                Debug.Log("[ClearMic] mic " + (MuteControl.Muted ? "muted" : "unmuted"));
            }

            float targetLevel = 0f;
            Recorder mic = NetworkSystem.Instance?.VoiceConnection?.PrimaryRecorder;
            if (mic != null && !MuteControl.Muted)
            {
                MicLimiterBehaviour limiter = mic.gameObject.GetComponent<MicLimiterBehaviour>();
                if (limiter != null)
                    targetLevel = limiter.CurrentLevel;
            }

            DisplayLevel = Mathf.Lerp(DisplayLevel, targetLevel, Time.deltaTime * 10f);

            if (Flashing)
                FlashTimer += Time.deltaTime;
        }
    }
    public class MicIndicator : MonoBehaviour
    {
        public static MicIndicator Instance;

        Texture2D micTex;
        Texture2D dotTex;
        bool texLoaded;

        AudioSource toggleSrc;
        AudioClip toggle1Clip;
        AudioClip toggle2Clip;
        bool nextIsToggle1 = true;

        void Awake()
        {
            Instance = this;
            StartCoroutine(LoadTexture());
            dotTex = MakeCircleTexture(32);

            toggleSrc = gameObject.AddComponent<AudioSource>();
            toggleSrc.spatialBlend = 0f;
            StartCoroutine(LoadToggleSounds());
        }

        IEnumerator LoadToggleSounds()
        {
            yield return LoadOggFromResource("toggle1.ogg", clip => toggle1Clip = clip);
            yield return LoadOggFromResource("toggle2.ogg", clip => toggle2Clip = clip);
        }

        IEnumerator LoadOggFromResource(string fileName, Action<AudioClip> onLoaded)
        {
            byte[] data = ReadEmbeddedResource(fileName);
            if (data == null)
            {
                Debug.LogWarning("[ClearMic] couldnt find embedded resource " + fileName);
                yield break;
            }

            string tempPath = Path.Combine(Application.temporaryCachePath, fileName);
            File.WriteAllBytes(tempPath, data);

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, AudioType.OGGVORBIS))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[ClearMic] failed to load clip " + fileName + ": " + www.error);
                    yield break;
                }

                onLoaded(DownloadHandlerAudioClip.GetContent(www));
            }
        }

        public void PlayToggleSound()
        {
            AudioClip clip = nextIsToggle1 ? toggle1Clip : toggle2Clip;
            nextIsToggle1 = !nextIsToggle1;

            if (clip != null)
                toggleSrc.PlayOneShot(clip);
        }

        static Texture2D MakeCircleTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f;
            Vector2 center = new Vector2(r, r);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(r - dist);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return tex;
        }

        IEnumerator LoadTexture()
        {
            byte[] data = ReadEmbeddedResource("mic.png");
            if (data == null)
            {
                Debug.LogWarning("[ClearMic] couldnt find mic.png");
                yield break;
            }

            micTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            micTex.LoadImage(data);
            micTex.filterMode = FilterMode.Bilinear;
            micTex.wrapMode = TextureWrapMode.Clamp;
            texLoaded = true;
            yield break;
        }

        static byte[] ReadEmbeddedResource(string fileName)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string resourceName = null;

            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(fileName))
                {
                    resourceName = name;
                    break;
                }
            }

            if (resourceName == null)
                return null;

            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        void Update()
        {
            IndicatorState.Tick();
        }

        void OnGUI()
        {
            if (!IndicatorState.Visible || !texLoaded)
                return;

            const float size = 30f;
            const float margin = 20f;

            float x = Screen.width - size - margin;
            float y = Screen.height - size - margin;

            bool muted = MuteControl.Muted;
            bool speaking = !muted && IndicatorState.DisplayLevel > 0.015f;

            float scale = speaking ? Mathf.Lerp(1f, 1.06f, Mathf.Clamp01(IndicatorState.DisplayLevel * 8f)) : 1f;

            Color iconColor;
            if (muted)
                iconColor = new Color(130f / 255f, 49f / 255f, 49f / 255f, 0.35f);
            else if (speaking)
                iconColor = new Color(1f, 1f, 1f, 0.4f);
            else
                iconColor = new Color(0.55f, 0.55f, 0.55f, 0.35f);

            Rect iconRect = new Rect(x, y, size, size);
            Vector2 pivot = new Vector2(x + size * 0.5f, y + size * 0.5f);

            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pivot);

            Color oldColor = GUI.color;
            GUI.color = iconColor;
            GUI.DrawTexture(iconRect, micTex, ScaleMode.ScaleToFit, true);
            GUI.color = oldColor;

            GUI.matrix = oldMatrix;

            DrawQualityDot(x, y, size);
        }

        void DrawQualityDot(float iconX, float iconY, float iconSize)
        {
            float alpha = IndicatorState.GetDotAlpha();
            if (alpha <= 0f)
                return;

            bool pcMode = QualityToggle.Instance != null && QualityToggle.Instance.PcMode;
            Color dotColor = pcMode ? new Color(0.3f, 0.5f, 1f) : new Color(0.3f, 1f, 0.4f);
            dotColor.a = alpha;

            const float dotSize = 7f;
            const float gap = 6f;
            float dotX = iconX - dotSize - gap;
            float dotY = iconY + iconSize * 0.5f - dotSize * 0.5f;

            Color oldColor = GUI.color;
            GUI.color = dotColor;
            GUI.DrawTexture(new Rect(dotX, dotY, dotSize, dotSize), dotTex, ScaleMode.StretchToFill, true);
            GUI.color = oldColor;
        }
    }
}