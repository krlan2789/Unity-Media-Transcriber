using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Whisper;
using static UnityEditor.Progress;

public class Transcribe : MonoBehaviour
{
    [Serializable]
    public struct DataContent
    {
        public TextAsset sampleContent;
        public AudioClip[] clips;
    }

    public Dropdown contentListDD;
    public Dropdown audiosDD;
    public Button transcribeBtn;
    public Button forceStopBtn;
    public Text sampleText;
    public Text subtitleText;
    public Text transcribedText;
    public Text timeText;
    public Image playingIcon;

    public WhisperManager whisper;
    public DataContent[] contents;

    private AudioClip[] clips;
    private List<string> sampleLines = new List<string>();

    private async void Awake()
    {
        whisper.enableTokens = true;
        whisper.tokensTimestamps = true;
        await whisper.InitModel();
    }

    private void Start()
    {
        // List of content text files
        contentListDD.ClearOptions();
        List<string> options = new List<string>();
        foreach (var datum in contents)
        {
            options.Add(datum.sampleContent.name);
        }
        contentListDD.AddOptions(options);
        contentListDD.onValueChanged.AddListener(OnContentListChanged);

        OnContentListChanged(0);

        playingIcon.gameObject.SetActive(false);

        forceStopBtn.onClick.AddListener(ForceStop);
    }

    private void OnContentListChanged(int index)
    {
        TextAsset sampleContent = contents[index].sampleContent;
        string contentText = ParseClcContent(sampleContent.text);
        clips = contents[index].clips;

        if (clips.Length > 0)
        {
            //  List of audio files selection
            audiosDD.ClearOptions();
            var options = new List<string>();

            if (clips.Length > 1)
                options.Add("All Audio");
            //else
            //    options.Add("Audio " + clips[0].name + ".ogg");

            foreach (var clip in clips) options.Add("Audio " + clip.name + ".ogg");
            audiosDD.AddOptions(options);

            //  List of sample text
            sampleLines.Clear();
            sampleLines.Add(contentText);
            if (clips.Length > 1) foreach (var line in contentText.Split("\n")) sampleLines.Add(line);
        }

        transcribeBtn.GetComponentInChildren<Text>().text = "Transcribe & Play";
        transcribeBtn.onClick.AddListener(() =>
        {
            AudioClip[] selectedClips = null;
            if (audiosDD.value == 0)
                selectedClips = clips;
            else
                selectedClips = new AudioClip[] { clips[audiosDD.value - 1] };

            Transcribing(selectedClips, sampleLines[audiosDD.value]);
        });
    }

    private string ParseClcContent(string content)
    {
        content = content.Replace("< ", "").Replace(" >", "").Replace("#", "");
        var parsed = new List<string>();
        foreach(string ch in content.Split(" "))
        {
            string selectedChar = "";
            
            if (ch.Length > 1) selectedChar = ch.Split("(")[0];
            else selectedChar = ch;

            parsed.Add(selectedChar);
        }

        return string.Join("", parsed.ToArray());
    }

    private void ForceStop()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private async void Transcribing(AudioClip[] clips, string sample)
    {
        subtitleText.text = "";
        transcribedText.text = "";
        timeText.text = "";
        sampleText.text = sample;

        transcribeBtn.interactable = false;
        audiosDD.interactable = false;

        var sw = new Stopwatch();
        sw.Start();

        foreach (AudioClip clip in clips)
        {
            await Transcribing(clip);
        }

        var time = sw.ElapsedMilliseconds;
        sw.Stop();
        timeText.text = $"{((float)time / 1000):0.000} seconds (Total)";

        transcribeBtn.GetComponentInChildren<Text>().text = "Transcribe & Play";
        transcribeBtn.interactable = true;
        audiosDD.interactable = true;
    }

    private async Task Transcribing(AudioClip clip)
    {
        transcribeBtn.GetComponentInChildren<Text>().text = "Transcribing..";
        var sw = new Stopwatch();
        sw.Start();

        // TODO: if you want to speed this up, subscribe to segments event
        // this code will transcribe whole text first
        var res = await whisper.GetTextAsync(clip);

        var time = sw.ElapsedMilliseconds;
        sw.Stop();
        timeText.text = $"{((float)time / 1000):0.000} seconds";

        // start playing sound
        AudioSource source = FindObjectOfType<AudioSource>();
        if (source == null) source = new GameObject("Audio Echo").AddComponent<AudioSource>();
        source.clip = clip;
        source.Play();
        playingIcon.gameObject.SetActive(true);

        transcribeBtn.GetComponentInChildren<Text>().text = "Playing audio..";
        subtitleText.text = ResultToRichText(res);

        // and show subtitles at the same time
        while (source.isPlaying)
        {
            //var text = GetSubtitles(res, source.time);
            //subtitleText.text = text;
            await Task.Yield();

            // check that audio source still here and wasn't destroyed
            if (!source)
                return;
        }
        playingIcon.gameObject.SetActive(false);

        timeText.text = "";
        subtitleText.text = "";
        transcribedText.text += AddSpaces(ResultToRichText(res)) + "\n";
    }

    // TODO: this isn't optimized and for demo use only
    private string GetSubtitles(WhisperResult res, float timeSec)
    {
        var sb = new StringBuilder();
        var time = TimeSpan.FromSeconds(timeSec);
        foreach (var seg in res.Segments)
        {
            // check if we already passed whole segment
            if (time >= seg.End)
            {
                sb.Append("" + seg.Text);
                continue;
            }

            foreach (var token in seg.Tokens)
            {
                if (time > token.Timestamp.Start)
                {
                    var text = token.Text;
                    sb.Append("" + text);
                }
            }
        }

        return "" + sb.ToString();
    }

    private string TokenToRichText(WhisperTokenData token)
    {
        if (token.IsSpecial)
            return "";

        var text = token.Text;
        var textColor = ProbabilityToColor(token.Prob);
        var richText = $"<color={textColor}>{text}</color>";
        return "" + richText;
    }

    private string ProbabilityToColor(float p)
    {
        if (p <= 0.33f)
            return "red";
        else if (p <= 0.66f)
            return "yellow";
        else
            return "green";
    }

    private string ResultToRichText(WhisperResult result)
    {
        var sb = new StringBuilder();
        foreach (var seg in result.Segments)
        {
            var str = seg.Text;
            sb.Append(str);
        }

        return "" + sb.ToString();
    }

    private string Clear(string text)
    {
        return "" + text.Replace(" ", "");//.Replace(",", "").Replace("?", "").Replace(".", "").Replace("。", "").Replace("、", "").Replace("！", "").Replace("：", "").Replace("，", "").Replace("，", "");
    }

    private string AddSpaces(string text)
    {
        for (int i = 1; i < text.Length; i++)
        {
            text = text.Insert(i, " ");
            i++;
        }

        return text;
    }
}
