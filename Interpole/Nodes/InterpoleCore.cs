#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using System.Linq;
using VVVV.Nodes;
using Interpole;

using VVVV.Core.Logging;
using System.Collections.Generic;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "Interpole Core",
                Category = "Interpole",
                Help = "",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class InterpoleCoreNode : IPluginEvaluate, IPartImportsSatisfiedNotification
    {
        #region fields & pins    

        [Input("InterpoleUI")]
        public Pin<InterpoleUI> FInterpoleIn;

        [Input("Play")]
        public IDiffSpread<bool> FPlay;

        [Input("Set Timecode")]
        public IDiffSpread<bool> FSetTimecode;

        [Input("Timecode")]
        public ISpread<InterpoleForm.TimeCode> FTimecode;

        [Input("Frame Clock", IsSingle = true)]
        public IDiffSpread<int> FExternalClock;

        [Input("Set Frame Clock")]
        public IDiffSpread<bool> FSetExternalClock;

        [Output("PlayHead (f)")]
        public ISpread<int> FPlayHead;

        [Output("InterpoleData")]
        public ISpread<InterpoleCoreNode> FInterpoleData;

        public int PlayHead = 0;
        public bool Play = false;
        public int FrameRate;
        public bool needsUpdate = false;

        public List<InterpoleForm.Clip> clipList = new List<InterpoleForm.Clip>();

        [Import()]
        public ILogger FLogger;

        #endregion fields & pins

        public void OnImportsSatisfied()
        {
            FInterpoleIn.Connected += Input_Connected;
            FInterpoleIn.Disconnected += Input_Disconnected;
        }

        private void Input_Disconnected(object sender, PinConnectionEventArgs args)
        {
            FLogger.Log(LogType.Debug, "DisConnected");
        }

        private void Input_Connected(object sender, PinConnectionEventArgs args)
        {
            FLogger.Log(LogType.Debug, "connected");
        }

        
        private void UpdateOutput()
        {
            //publish playhead of core
            FPlayHead[0] = PlayHead;

            //update enums
            needsUpdate = FInterpoleIn[0].needsUpdate;

            // get clip list from UI
            clipList = FInterpoleIn[0].interpoleForm.ipD.ClipList;

            FrameRate = FInterpoleIn[0].interpoleForm.ipD.FrameRate;

            //publish core data
            FInterpoleData[0] = this; 
        }


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            // only do this when there is a connection to the UI
            if (FInterpoleIn.IsConnected && FInterpoleIn[0] != null) 
            {
                // if playing, set UI playhead
                if (FInterpoleIn[0].interpoleForm.play || FPlay[0])
                {
                    //iterate playhead
                    PlayHead++;
                    Play = true;
                    //set playhead
                    FInterpoleIn[0].interpoleForm.ipD.PlayHead = PlayHead;
                }
                //if not playing, get UI playhead position
                else
                {
                    PlayHead = FInterpoleIn[0].interpoleForm.ipD.PlayHead;
                    Play = false;
                }

                //update all outputs
                UpdateOutput();
            }

            //FLogger.Log(LogType.Debug, "nope"); 


        }

    }
}

