//------------------------------------------------------------------------------
// <copyright file="SpeechRecognizer.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

// This module provides sample code used to demonstrate the use
// of the KinectAudioSource for speech recognition in a game setting.

// IMPORTANT: This sample requires the Speech Platform SDK (v11) to be installed on the developer workstation

namespace ShapeGame.Speech
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Microsoft.Kinect;
    using Microsoft.Speech.AudioFormat;
    using Microsoft.Speech.Recognition;
    using ShapeGame.Utils;

    public class SpeechRecognizer : IDisposable
    {
        private readonly Dictionary<string, WhatSaid> commandPhrases = new Dictionary<string, WhatSaid>
            {
                { "Go to", new WhatSaid { Verb = Verbs.GoToPlace } },
                { "Navigate to", new WhatSaid { Verb = Verbs.NavigateTo } },
                { "Finish", new WhatSaid { Verb = Verbs.Finish} },

            };

        private readonly Dictionary<string, WhatSaid> placePhrases = new Dictionary<string, WhatSaid>
            {
                { "London", new WhatSaid { Verb = Verbs.Placerice, Place = Places.London , Longitude= 51.5074, Latitude= 0.1278} },
                { "Home", new WhatSaid { Verb = Verbs.Placerice, Place = Places.Home , Longitude= 52.513269, Latitude= 13.437949} },
                { "University", new WhatSaid { Verb = Verbs.Placerice, Place = Places.University, Longitude= 52.512235, Latitude= 13.326266} },
                { "Berlin", new WhatSaid { Verb = Verbs.Placerice, Place = Places.Berlin , Longitude= 52.520, Latitude= 13.4050} },
                { "New York", new WhatSaid { Verb = Verbs.Placerice, Place = Places.NewYork , Longitude= 40.7128, Latitude= -74.0059} },
                { "San Francisco", new WhatSaid { Verb = Verbs.Placerice, Place = Places.SanFrancisco , Longitude= 37.7749, Latitude= -122.4194} },
            };
        
        private SpeechRecognitionEngine sre;
        private KinectAudioSource kinectAudioSource;
        private bool isDisposed;

        private SpeechRecognizer()
        {
            RecognizerInfo ri = GetKinectRecognizer();
            this.sre = new SpeechRecognitionEngine(ri);
            this.LoadGrammar(this.sre);
        }

        public event EventHandler<SaidSomethingEventArgs> SaidSomething;

        public enum Places
        {
            None = 0,
            London,
            Berlin,
            NewYork,
            SanFrancisco,
            University, 
            Home
        }

        public enum Verbs
        {
            None = 0,
            NavigateTo,
            GoToPlace,
            Placerice,
            Finish
        }

        public EchoCancellationMode EchoCancellationMode
        {
            get
            {
                this.CheckDisposed();

                if (this.kinectAudioSource == null)
                {
                    return EchoCancellationMode.None;
                }

                return this.kinectAudioSource.EchoCancellationMode;
            }

            set
            {
                this.CheckDisposed();

                if (this.kinectAudioSource != null)
                {
                    this.kinectAudioSource.EchoCancellationMode = value;
                }
            }
        }
        
        public static SpeechRecognizer Create()
        {
            SpeechRecognizer recognizer = null;

            try
            {
                recognizer = new SpeechRecognizer();
            }
            catch (Exception)
            {
                // speech prereq isn't installed. a null recognizer will be handled properly by the app.
            }

            return recognizer;
        }

        public void Start(KinectAudioSource kinectSource)
        {
            this.CheckDisposed();

            if (kinectSource != null)
            {
                this.kinectAudioSource = kinectSource;
                this.kinectAudioSource.AutomaticGainControlEnabled = false;
                this.kinectAudioSource.BeamAngleMode = BeamAngleMode.Adaptive;
                var kinectStream = this.kinectAudioSource.Start();
                this.sre.SetInputToAudioStream(
                    kinectStream, new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
                this.sre.RecognizeAsync(RecognizeMode.Multiple);
            }
        }

        public void Stop()
        {
            this.CheckDisposed();

            if (this.sre != null)
            {
                if (this.kinectAudioSource != null)
                {
                    this.kinectAudioSource.Stop();
                }

                this.sre.RecognizeAsyncCancel();
                this.sre.RecognizeAsyncStop();

                this.sre.SpeechRecognized -= this.SreSpeechRecognized;
                this.sre.SpeechHypothesized -= this.SreSpeechHypothesized;
                this.sre.SpeechRecognitionRejected -= this.SreSpeechRecognitionRejected;
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "sre",
            Justification = "This is suppressed because FXCop does not see our threaded dispose.")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Stop();

                if (this.sre != null)
                {
                    // NOTE: The SpeechRecognitionEngine can take a long time to dispose
                    // so we will dispose it on a background thread
                    ThreadPool.QueueUserWorkItem(
                        delegate(object state)
                            {
                                IDisposable toDispose = state as IDisposable;
                                if (toDispose != null)
                                {
                                    toDispose.Dispose();
                                }
                            },
                            this.sre);
                    this.sre = null;
                }

                this.isDisposed = true;
            }
        }

        private static RecognizerInfo GetKinectRecognizer()
        {
            Func<RecognizerInfo, bool> matchingFunc = r =>
            {
                string value;
                r.AdditionalInfo.TryGetValue("Kinect", out value);
                return "True".Equals(value, StringComparison.InvariantCultureIgnoreCase) && "en-US".Equals(r.Culture.Name, StringComparison.InvariantCultureIgnoreCase);
            };
            return SpeechRecognitionEngine.InstalledRecognizers().Where(matchingFunc).FirstOrDefault();
        }

        private void CheckDisposed()
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException("SpeechRecognizer");
            }
        }

        private void LoadGrammar(SpeechRecognitionEngine speechRecognitionEngine)
        {
            // Build a simple grammar of shapes, colors, and some simple program control
            var commands = new Choices();
            foreach (var phrase in this.commandPhrases)
            {
                commands.Add(phrase.Key);
            }

            var places = new Choices();
            foreach (var phrase in this.placePhrases)
            {
                places.Add(phrase.Key);
            }
            
            var placeGrammar = new GrammarBuilder();
            placeGrammar.Append(commands);
            placeGrammar.Append(places);

            var objectChoices = new Choices();
            objectChoices.Add(commands);
            objectChoices.Add(places);
            objectChoices.Add(placeGrammar);

            var actionGrammar = new GrammarBuilder();
            actionGrammar.AppendWildcard();
            actionGrammar.Append(objectChoices);

            var allChoices = new Choices();
            allChoices.Add(actionGrammar);
            
            // This is needed to ensure that it will work on machines with any culture, not just en-us.
            var gb = new GrammarBuilder { Culture = speechRecognitionEngine.RecognizerInfo.Culture };
            gb.Append(allChoices);

            var g = new Grammar(gb);
            speechRecognitionEngine.LoadGrammar(g);
            speechRecognitionEngine.SpeechRecognized += this.SreSpeechRecognized;
            speechRecognitionEngine.SpeechHypothesized += this.SreSpeechHypothesized;
            speechRecognitionEngine.SpeechRecognitionRejected += this.SreSpeechRecognitionRejected;
        }

        private void SreSpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            var said = new SaidSomethingEventArgs { Verb = Verbs.None, Matched = "?" };

            if (this.SaidSomething != null)
            {
                this.SaidSomething(new object(), said);
            }

            Console.WriteLine("\nSpeech Rejected");
        }

        private void SreSpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
        {
            Console.Write("\rSpeech Hypothesized: \t{0}", e.Result.Text);
        }

        private void SreSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            Console.Write("\rSpeech Recognized: \t{0}", e.Result.Text);

            if ((this.SaidSomething == null) || (e.Result.Confidence < 0.3))
            {
                return;
            }

            var said = new SaidSomethingEventArgs
                { RgbColor = System.Windows.Media.Color.FromRgb(0, 0, 0), Place = 0, Verb = 0, Phrase = e.Result.Text };

            // First check for color, in case both color _and_ shape were both spoken
            foreach (var phrase in this.placePhrases)
            {
                if (e.Result.Text.Contains(phrase.Key))
                {
                    said.Place = phrase.Value.Place;
                    said.Longitude = phrase.Value.Longitude;
                    said.Latitude = phrase.Value.Latitude;
                    said.Matched = phrase.Key;
                    break;
                }
            }

            // Look for a match in the order of the lists below, first match wins.
            List<Dictionary<string, WhatSaid>> allDicts = new List<Dictionary<string, WhatSaid>> { this.commandPhrases};

            bool found = false;
            for (int i = 0; i < allDicts.Count && !found; ++i)
            {
                foreach (var phrase in allDicts[i])
                {
                    if (e.Result.Text.Contains(phrase.Key))
                    {
                        said.Verb = phrase.Value.Verb;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                return;
            }

            if (this.SaidSomething != null)
            {
                this.SaidSomething(new object(), said);
            }
        }
        
        private struct WhatSaid
        {
            public Verbs Verb;
            public Places Place;

            public double Longitude { get; internal set; }
            public double Latitude { get; internal set; }
        }

        public class SaidSomethingEventArgs : EventArgs
        {
            public Verbs Verb { get; set; }

            public Places Place { get; set; }
            public Double Latitude { get; set; }
            public Double Longitude { get; set; }

            public System.Windows.Media.Color RgbColor { get; set; }

            public string Phrase { get; set; }

            public string Matched { get; set; }
        }
    }
}