using System;
using System.Threading;
using System.ComponentModel.Composition;
using System.Windows.Forms;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using Interpole;
using VVVV.Core.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

namespace VVVV.Nodes
{
    [PluginInfo(Name = "Interpole UI", Category = "Interpole", AutoEvaluate = true)]

    public class InterpoleUI : IPluginEvaluate, IDisposable
    {
        Thread FUIThread;
        public InterpoleBase interpoleForm;

        [Config("InterpoleData")]
        public ISpread<string> InterpoleXML;

        [Output("Out")]
        ISpread<InterpoleUI> FInterpoleOut;

        [Output("XML", Visibility = PinVisibility.Hidden)]
        ISpread<string> FXMLOut;

        public bool needsUpdate = false; //inform connected nodes something changed
        int counter = 0;

        [Import()]
        public ILogger FLogger;

        public InterpoleUI()
        {
            FUIThread = new Thread(InitializeForm);
            FUIThread.SetApartmentState(ApartmentState.STA);
            FUIThread.Priority = ThreadPriority.Lowest;
            FUIThread.Start();
        }

        public void Dispose()
        {
            if (interpoleForm != null)
                interpoleForm.Invoke((Action)CleanupForm);
        }

        void InitializeForm()
        {
            interpoleForm = new InterpoleBase();
            interpoleForm.Changed += new InterpoleBase.ChangedHandler(UpdateConfigs);

            //load saved interpole settings
            if (InterpoleXML[0].Length > 10)
            {
                interpoleForm.ipD = InterpoleBase.DeserializeInterpoleData(InterpoleXML[0]);
                FXMLOut[0] = InterpoleXML[0]; //update XML Output
                interpoleForm.Location = interpoleForm.ipD.WLocation;
                interpoleForm.Size = interpoleForm.ipD.WSize;

                Debug.WriteLine("import sucessful");
            }
          

            // Use ShowDialog as it starts its own main loop - using Show alone the window just disappears
            interpoleForm.ShowDialog();
        }

        //set vvvv config pins with fresh XML data
        private void UpdateConfigs(InterpoleBase interpoleForm, EventArgs e)
        {
            InterpoleXML[0] = interpoleForm.InterpoleDataSerialized;
            FXMLOut[0] = InterpoleXML[0];
            needsUpdate = true;
        }

        void CleanupForm()
        {
            interpoleForm.Dispose();
        }

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if (interpoleForm == null)
                return; // Form not created yet
            else
            {
                interpoleForm.Invalidate(); //redraw UI from here, not sure if this makes sense

                //handle update variable
                if(needsUpdate)
                {
                    //FLogger.Log(LogType.Debug, "needsUpdate InterpoleUI");
                    counter++;
                    if(counter >= 2)
                    {
                        counter = 0;
                        needsUpdate = false;
                    }
                }

                //output everything
                FInterpoleOut[0] = this;
            }
        }
    }
}