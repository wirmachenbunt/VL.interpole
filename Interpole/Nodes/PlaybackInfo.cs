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
    [PluginInfo(Name = "PlaybackInfo",
                Category = "Clip Animation",
                Help = "get a clip information",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class PlaybackInfo : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins
        

        [Input("Interpole")]
        public Pin<Interpole> InterpoleIn;

        [Output("Play")]
        public ISpread<bool> FPlay;

        [Output("Seconds")]
        public ISpread<double> FSeconds;

        [Output("TimeJump")]
        public ISpread<bool> FTimeJump;

        int tempPlayPosFrames = 0;
        bool wasPlaying = false;

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

       

     

        public void UpdateOutputs()
        {
            try // fixes missing clip bug, when deleting a choosen clip
            {
                FPlay[0] = InterpoleIn[0].play;
                FSeconds[0] = (double)InterpoleIn[0].playPosFrames / InterpoleIn[0].Framerate[0];

                // Timejump when looping
                
                if(!wasPlaying && FPlay[0] ||
                    !InterpoleIn[0].FSetTimecode[0] && InterpoleIn[0].FIsLoop[0] &&
                InterpoleIn[0].playPosFrames == InterpoleIn[0].FLoopRegion[0] ||
                (Math.Abs(tempPlayPosFrames - InterpoleIn[0].playPosFrames) > 1)) FTimeJump[0] = true;
                else FTimeJump[0] = false;

                wasPlaying = FPlay[0];
                tempPlayPosFrames = InterpoleIn[0].playPosFrames;
                //FLogger.Log(LogType.Debug, "GetClip");
            }
            catch { }
        }


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {


            if (InterpoleIn.IsConnected && InterpoleIn[0].clipList != null)
            {
                if (InterpoleIn[0].clipList.Count > 0)
                {            
                    UpdateOutputs();
                }
            }
        }



    }
}
