#region usings

using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using System.Linq;
using VVVV.Nodes;
using System.Threading;
using VVVV.Core.Logging;
using System.ComponentModel;

#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "GetClipBuffered",
                Category = "Clip Animation",
                Help = "get a clip information",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class GetClipBuffered : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins
        protected IDiffSpread<EnumEntry> EnumClip;

        [Input("Interpole")]
        public Pin<Interpole> InterpoleIn;

        [Output("IsActive")]
        public ISpread<bool> IsActive;

        [Output("FadeInOut")]
        public ISpread<double> FadeOut;

        Interpole.Clip myclip = new Interpole.Clip();
        List<Interpole.Clip> clipsWithSameName = new List<Interpole.Clip>();
        List<bufferedClips> renderedClipList = new List<bufferedClips>();

        BackgroundWorker worker;

        [Import()]
        public ILogger FLogger;
        [Import()]
        public IIOFactory FIOFactory;
        [Import()]
        IPluginHost FHost;

        string EnumName;
        bool invalidate = false;
        bool init = true;
        bool tempplay = false;

        List<int> startFrames = new List<int>();
        List<int> endFrames = new List<int>();

        #endregion fields & pins

        public void OnImportsSatisfied()
        {
            InterpoleIn.Connected += Input_Connected;
            InterpoleIn.Disconnected += Input_Disconnected;

            FHost.GetNodePath(true, out EnumName); //get unique node path
            EnumName += "Interpole"; // add unique name to path
            InputAttribute attr = new InputAttribute("Clip"); //name of pin
            attr.EnumName = EnumName;
            EnumClip = FIOFactory.CreateDiffSpread<EnumEntry>(attr);


            ///////////Background Threading Stuff /////////////////////////////////////////////////////////

            worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;

            worker.DoWork += new DoWorkEventHandler(worker_DoWork);
            worker.ProgressChanged += new ProgressChangedEventHandler(worker_ProgressChanged);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(worker_RunWorkerCompleted);
            ///////////Background Threading Stuff /////////////////////////////////////////////////////////
        }

        void worker_DoWork(object sender, DoWorkEventArgs e)
        {

            
            FillBufferList();
            
            e.Result = "done";
        }

        void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //do something here
        }

        void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //do something here
            //FLogger.Log(LogType.Debug, "Worker complete");
        }

        private void Input_Disconnected(object sender, PinConnectionEventArgs args)
        {
            FLogger.Log(LogType.Debug, "DisConnected");
            //invalidate = true;
        }

        private void Input_Connected(object sender, PinConnectionEventArgs args)
        {
            invalidate = true;
            FLogger.Log(LogType.Debug, "connected");
        }

        public void UpdateEnum()
        {
            if (invalidate || InterpoleIn[0].needsUpdate)
            {
                EnumManager.UpdateEnum(EnumName, InterpoleIn[0].clipList[0].Name, InterpoleIn[0].clipList.Select(x => x.Name).Distinct().ToArray());
                invalidate = false;
                // FLogger.Log(LogType.Debug, "Update GetClip Enum");
                if (InterpoleIn[0].needsUpdate)
                {
                    if (worker.IsBusy)
                    {
                        worker.CancelAsync();
                    }
                    else
                    {
                        worker.RunWorkerAsync();
                    }
                }
            }
        }

        public static int Clamp(int value, int min, int max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        public double CalcFade(int position, int start, int duration, int fadein, int fadeout)
        {
            double output = 0;
            //is position matching fadein of clip
            if (position >= start) output = ((double)1 / fadein) * (position - start);
            if (position > (start + fadein)) output = 1.0;
            if (position > ((duration + start) - fadeout) && position < (start + duration)) output = 1 - (position - (duration + start - fadeout)) * ((double)1 / fadeout);
            if (position >= (duration + start)) output = 0;
            if (double.IsNaN(output)) output = 1;
            

            return output;
        }

        public void FillBufferList()
        {
            //clear renderbuffer
            renderedClipList.Clear();

            for (int s = 0; s < EnumClip.SliceCount; s++) // iterate through enum clip
            {
                //filter clips by enum name
                clipsWithSameName = InterpoleIn[0].clipList.Where(x => x.Name == EnumClip[s]).ToList();

                //create new buffered clip and retrieve absolut start and endframe of this cliplist
                renderedClipList.Add(new bufferedClips()
                {
                    startFrame = clipsWithSameName.Min(x => x.StartFrame),
                    endFrame = clipsWithSameName.Max(x => x.StartFrame + x.Duration),
                    IsActive = new List<bool>(),
                    Fade = new List<double>()
                });

                //set duration
                renderedClipList[s].duration = renderedClipList[s].endFrame - renderedClipList[s].startFrame;


                //render active part
                bool hit = false;
                for (int i = renderedClipList[s].startFrame; i < renderedClipList[s].endFrame; i++)
                {
                    //active or not
                    hit = clipsWithSameName.Any(x => x.StartFrame <= i && (x.StartFrame + x.Duration > i)); //hits a clip at framepos
                    renderedClipList[s].IsActive.Add(hit);

                    //calculate fade
                    var clip = clipsWithSameName.Where(x => x.StartFrame <= i && (x.StartFrame + x.Duration > i)).FirstOrDefault(); // get clip at framepos

                    //interpolate fade and add calculated frames
                    if (clip != null) renderedClipList[s].Fade.Add(CalcFade(i, clip.StartFrame, clip.Duration, clip.FadeIn, clip.FadeOut));
                    else renderedClipList[s].Fade.Add(0);


                }
            }

            //FLogger.Log(LogType.Debug, "rendered buffer");

        }

        public void UpdateOutputs()
        {
            IsActive.SliceCount = 0;
            FadeOut.SliceCount = 0;

            for (int i = 0; i < renderedClipList.Count; i++)
            {
                //is clip rendered, then do output
                if (renderedClipList[0].IsActive.Count > 0)
                {
                    //set all sliceCounts to zero

                    try
                    {
                        int pos = Clamp(InterpoleIn[0].playPosFrames - renderedClipList[i].startFrame, 0, renderedClipList[i].duration);

                    IsActive.Add(false);

                    if (pos < renderedClipList[0].IsActive.Count) IsActive[i] = renderedClipList[i].IsActive[pos];

                    if (InterpoleIn[0].playPosFrames < renderedClipList[i].startFrame) IsActive[i] = false;
                    else if (InterpoleIn[0].playPosFrames > renderedClipList[i].endFrame) IsActive[i] = false;

                    FadeOut.Add(0);

                    if (pos < renderedClipList[i].Fade.Count) FadeOut[i] = renderedClipList[i].Fade[pos];
                    else FadeOut[i] = 0;
                    }
                    catch
                    {

                        
                    }
                    



                    //FLogger.Log(LogType.Debug, pos.ToString() + " is active count " + renderedClipList[0].IsActive.Count.ToString());
                }
            }
        }

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {


            if (InterpoleIn.IsConnected && InterpoleIn[0].clipList != null)
            {
                UpdateEnum();

                //if (InterpoleIn[0].needsUpdate) FillBufferList();

                if (InterpoleIn[0].clipList.Count > 0 && renderedClipList.Count > 0) UpdateOutputs();

            }
        }
    }

    // Buffered clip object hold prerendered values to save performance.

    public class bufferedClips
    {
        public int startFrame
        {
            get;
            set;
        }

        public int endFrame
        {
            get;
            set;
        }

        public int duration
        {
            get;
            set;
        }

        public List<bool> IsActive
        {
            get;
            set;
        }

        public List<double> Fade
        {
            get;
            set;
        }

    }
}
