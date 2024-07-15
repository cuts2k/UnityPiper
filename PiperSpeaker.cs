using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Abuksigun.Piper
{
    public class PiperSpeaker
    {
        readonly AudioClip audioClip;
        readonly PiperVoice voice;

        readonly List<float[]> pcmBuffers = new List<float[]>();
        volatile int pcmBufferPointer = 0;
        volatile string queuedText = null;
        Task speechTask = null;
        System.Object speechTaskLock = new System.Object();

        // Event that gets fired in the else block of PCMRead
        public event Action StoppedPlaying;

        public AudioClip AudioClip => audioClip;

        public unsafe PiperSpeaker(PiperVoice voice, int bufferSize = 1024)
        {
            this.voice = voice;
            PiperLib.SynthesisConfig synthesisConfig = PiperLib.getSynthesisConfig(voice.Voice);
            audioClip = AudioClip.Create("MyPCMClip", bufferSize * 24, synthesisConfig.channels, synthesisConfig.sampleRate, true, PCMRead);
        }

        ~PiperSpeaker()
        {
            if (audioClip)
                UnityEngine.Object.Destroy(audioClip);
        }

        // Use when you want to interrupt the current speech and say new replica
        public unsafe Task Speak(string text, Int64 speakerId = -1)
        {
            if (speakerId >= 0)
            {
                PiperLib.setSpeakerId(voice.Voice, speakerId);
            }

            lock (pcmBuffers)
            {
                pcmBuffers.Clear();
                pcmBufferPointer = 0;
            }

            return ContinueSpeech(text);
        }

        // Use when you want to add more text to the current speech
        public unsafe Task ContinueSpeech(string text)
        {
            lock (speechTaskLock)
            {
                if (speechTask == null || speechTask.IsCompleted)
                {
                    speechTask = Task.Run(() =>
                    {
                        do
                        {
                            voice.TextToAudioStream(text, (short* data, int length) => AddPCMData(data, length));
                            text = queuedText;
                            queuedText = null;
                        }
                        while (text != null);
                    });
                }
                else
                {
                    queuedText = text;
                }
            }
            return speechTask;
        }

        void PCMRead(float[] data)
        {
            if (pcmBuffers.Count == 0)
            {
                Array.Fill(data, 0);
                return;
            }

            int dataLength = data.Length;
            int dataIndex = 0;

            while (dataIndex < dataLength)
            {
                lock (pcmBuffers)
                {
                    int bufferIndex = 0;
                    int bufferOffset = pcmBufferPointer;

                    while (bufferIndex < pcmBuffers.Count && bufferOffset >= pcmBuffers[bufferIndex].Length)
                    {
                        bufferOffset -= pcmBuffers[bufferIndex].Length;
                        bufferIndex++;
                    }

                    if (bufferIndex < pcmBuffers.Count)
                    {
                        float[] currentBuffer = pcmBuffers[bufferIndex];
                        int remainingInBuffer = currentBuffer.Length - bufferOffset;
                        int remainingInData = dataLength - dataIndex;
                        int copyLength = Mathf.Min(remainingInBuffer, remainingInData);

                        Array.Copy(currentBuffer, bufferOffset, data, dataIndex, copyLength);

                        dataIndex += copyLength;
                        pcmBufferPointer += copyLength;
                    }
                    else
                    {
                        Array.Fill(data, 0, dataIndex, data.Length - dataIndex);
                        StoppedPlaying?.Invoke();
                        break;
                    }
                }
            }
        }

        public unsafe void AddPCMData(short* pcmData, int length)
        {
            float[] floatData = new float[length];
            for (int i = 0; i < length; i++)
                floatData[i] = pcmData[i] / 32768.0f;
            lock (pcmBuffers)
                pcmBuffers.Add(floatData);
        }
    }

}