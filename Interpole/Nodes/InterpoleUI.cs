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

namespace VVVV.Nodes
{
    [PluginInfo(Name = "Interpole UI", Category = "Interpole", AutoEvaluate = true)]

    public class InterpoleUI : IPluginEvaluate, IDisposable
    {
        Thread FUIThread;
        public InterpoleForm interpoleForm;

        [Output("XML")]
        ISpread<string> FXMLOut;

        [Output("Out")]
        ISpread<InterpoleUI> FInterpoleOut;

        public InterpoleUI()
        {
            FUIThread = new Thread(InitializeForm);
            FUIThread.SetApartmentState(ApartmentState.STA);            
            FUIThread.Start();          
        }

        public void Dispose()
        {
            if (interpoleForm != null)
                interpoleForm.Invoke((Action)CleanupForm);
        }

        void InitializeForm()
        {
            interpoleForm = new InterpoleForm();
            interpoleForm.ChangedInterpoleData += new InterpoleForm.ChangedInterpoleHandler(UpdateConfigs);
            
            // Use ShowDialog as it starts its own main loop - using Show alone the window just disappears
            interpoleForm.ShowDialog();
        }

        private void UpdateConfigs(InterpoleForm interpoleForm, EventArgs e)
        {
            Debug.WriteLine("Event raised");
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
                FXMLOut[0] = interpoleForm.InterpoleXML;           
                FInterpoleOut[0] = this;
            }
            //FMyForm.Invoke((Action)InvalidateForm);
            //FMyForm.Invoke((Action)(() => FMyLabel.Text = value));       
            
        }
    }
}