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
        public ISpread<InterpoleBase.TimeCode> FTimecode;

        [Input("Frame Clock", IsSingle = true)]
        public IDiffSpread<int> FExternalClock;

        [Input("Set Frame Clock")]
        public IDiffSpread<bool> FSetExternalClock;

        [Input("XML")]
        public IDiffSpread<string> FXMLIn;

        [Output("PlayHead (f)")]
        public ISpread<int> FPlayHead;

        [Output("InterpoleData")]
        public ISpread<InterpoleCoreNode> FInterpoleData;

        public int PlayHead = 0;
        public bool Play = false;
        public int FrameRate;
        public bool needsUpdate = false;

        public List<InterpoleBase.Clip> clipList = new List<InterpoleBase.Clip>(); //just send the clips to getclip

        InterpoleBase.InterpoleData ipD = new InterpoleBase.InterpoleData(); // boygroup case, no access to UI interpole data

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

            #region UI Case
            // only do this when there is a connection to the UI
            if (FInterpoleIn.IsConnected && FInterpoleIn[0] != null && FXMLIn[0].Length < 10)
            {
                // if playing, set UI playhead
                if (FInterpoleIn[0].interpoleForm.play || FPlay[0])
                {
                    //iterate playhead
                    PlayHead++;
                    Play = true;

                    //handle Looping
                    if (FInterpoleIn[0].interpoleForm.ipD.Loop && PlayHead > FInterpoleIn[0].interpoleForm.ipD.LoopEnd) PlayHead = FInterpoleIn[0].interpoleForm.ipD.LoopStart;

                    //set UI playhead
                    FInterpoleIn[0].interpoleForm.ipD.PlayHead = PlayHead;
                }
                //if not playing, get UI playhead position
                else
                {
                    PlayHead = FInterpoleIn[0].interpoleForm.ipD.PlayHead;
                    Play = false;
                }

                if (FSetTimecode[0] && FTimecode[0] != null)
                {
                    Play = false;
                    PlayHead = FTimecode[0].Frames + (FTimecode[0].Seconds * FrameRate) + (FTimecode[0].Minutes * 60 * FrameRate) + (FTimecode[0].Hours * 60 * 60 * FrameRate);
                    FInterpoleIn[0].interpoleForm.ipD.PlayHead = PlayHead; //set UI playhead
                }


                //update all outputs
                UpdateOutput();
            }
            #endregion

            #region Boygroup Case

            if(FXMLIn.IsChanged)
            {
                ipD = InterpoleBase.DeserializeInterpoleData(FXMLIn[0]);
                needsUpdate = true;
            }

            if (FXMLIn[0].Length > 10)
            {
                // if playing, set UI playhead
                if (FPlay[0])
                {
                    //iterate playhead
                    PlayHead++;
                    Play = true;            
                }
                //if not playing, get UI playhead position
                else
                {
                    Play = false;
                }

                if (FSetTimecode[0] && FTimecode[0] != null)
                {
                    Play = false;
                    PlayHead = FTimecode[0].Frames + (FTimecode[0].Seconds * FrameRate) + (FTimecode[0].Minutes * 60 * FrameRate) + (FTimecode[0].Hours * 60 * 60 * FrameRate);
                }

                //publish playhead of core
                FPlayHead[0] = PlayHead;

                // get clip list from UI
                clipList = ipD.ClipList;

                FrameRate = ipD.FrameRate;

                //publish core data
                FInterpoleData[0] = this;
            }
            #endregion

        }

    }
}

