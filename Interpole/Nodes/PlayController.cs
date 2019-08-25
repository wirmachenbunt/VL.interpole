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


using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "PlayController",
                Category = "Interpole",
                Help = "get a clip information",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class PlayController : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins


        [Input("Interpole")]
        public Pin<InterpoleCoreNode> InterpoleIn;

        [Input("Next")]
        public IDiffSpread<bool> FNext;

        [Input("Previous")]
        public IDiffSpread<bool> FPrevious;

        [Input("Play")]
        public IDiffSpread<bool> FPlay;

        [Input("Stop")]
        public IDiffSpread<bool> FStop;

        List<Interpole.InterpoleBase.Clip> stopClips = new List<Interpole.InterpoleBase.Clip>();
        List<Interpole.InterpoleBase.Clip> loopClips = new List<Interpole.InterpoleBase.Clip>();

        [Import()]
        public ILogger FLogger;
        [Import()]
        public IIOFactory FIOFactory;
        [Import()]
        IPluginHost FHost;

        #endregion fields & pins

        public void OnImportsSatisfied()
        {
            InterpoleIn.Connected += Input_Connected;
            InterpoleIn.Disconnected += Input_Disconnected;


        }

        private void Input_Disconnected(object sender, PinConnectionEventArgs args)
        {
            FLogger.Log(LogType.Debug, "DisConnected");
            //invalidate = true;
        }

        private void Input_Connected(object sender, PinConnectionEventArgs args)
        {

            FLogger.Log(LogType.Debug, "connected");
        }


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {


            if (InterpoleIn.IsConnected && InterpoleIn[0].clipList != null)
            {
                if (InterpoleIn[0].clipList.Count > 0)
                {
                    stopClips = InterpoleIn[0].clipList.FindAll(x => x.Name == "Stop");
                    loopClips = InterpoleIn[0].clipList.FindAll(x => x.Name == "Loop");

                    Interpole.InterpoleBase.Clip loopClip = loopClips.FirstOrDefault(x => x.StartFrame < InterpoleIn[0].PlayHead && x.StartFrame + x.Duration > InterpoleIn[0].PlayHead);

                    if (loopClip != null && InterpoleIn[0].PlayHead > loopClip.StartFrame + loopClip.Duration - 2)
                    {
                        //FLogger.Log(LogType.Debug, "foundsomething");
                        InterpoleIn[0].PlayHead = loopClip.StartFrame;
                    }

                       

                    if (stopClips.Any(x => x.StartFrame == InterpoleIn[0].FPlayHead[0] + 1) && InterpoleIn[0].playExt)
                    {                 
                        InterpoleIn[0].playExt = false;
                    }

                    if (FNext.IsChanged && FNext[0])
                    {
                        // find all stop clips
                        stopClips = InterpoleIn[0].clipList.FindAll(x => x.Name == "Stop");
                        stopClips = stopClips.OrderBy(x => x.StartFrame).ToList();

                        //find the one greater to the playhead
                        Interpole.InterpoleBase.Clip nextClip = stopClips.FirstOrDefault(x => x.StartFrame > InterpoleIn[0].PlayHead);

                        if (nextClip != null)

                        {
                            InterpoleIn[0].PlayHead = nextClip.StartFrame;
                            //set playhead

                            //do play
                            InterpoleIn[0].playExt = true;
                        }

                    }
                    if (FPrevious.IsChanged && FPrevious[0])
                    {
                        // find all stop clips
                        stopClips = InterpoleIn[0].clipList.FindAll(x => x.Name == "Stop");
                        stopClips = stopClips.OrderBy(x => x.StartFrame).ToList();
                        Interpole.InterpoleBase.Clip previousClip = new Interpole.InterpoleBase.Clip();

                        try
                        {
                            //find the one greater to the playhead
                            previousClip = stopClips.TakeWhile(x => x.StartFrame < InterpoleIn[0].PlayHead).Last();
                            //set playhead
                            InterpoleIn[0].PlayHead = previousClip.StartFrame - 3;

                            //FLogger.Log(LogType.Debug, previousClip.StartFrame.ToString());

                            //do play
                            InterpoleIn[0].playExt = true;
                        }
                        catch (Exception)
                        {
                            

                        }
                        
             
                    }

                    if (FPlay.IsChanged && FPlay[0] && !InterpoleIn[0].playExt)
                    {
                        InterpoleIn[0].playExt = true;
                    }

                    if (FStop.IsChanged && FStop[0] && InterpoleIn[0].playExt)
                    {
                        InterpoleIn[0].playExt = false;
                    }

                }
            }
        }



    }
}
