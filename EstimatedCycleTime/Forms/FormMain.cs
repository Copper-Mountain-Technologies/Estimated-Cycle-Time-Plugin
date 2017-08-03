// Copyright ©2015-2017 Copper Mountain Technologies
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
// ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using CopperMountainTech;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EstimatedCycleTime
{
    public partial class FormMain : Form
    {
        // ::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::

        private enum ComConnectionStateEnum
        {
            INITIALIZED,
            NOT_CONNECTED,
            CONNECTED_VNA_NOT_READY,
            CONNECTED_VNA_READY
        }

        private ComConnectionStateEnum previousComConnectionState = ComConnectionStateEnum.INITIALIZED;
        private ComConnectionStateEnum comConnectionState = ComConnectionStateEnum.NOT_CONNECTED;

        // ::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::

        public FormMain()
        {
            InitializeComponent();

            // --------------------------------------------------------------------------------------------------------

            // set form icon
            Icon = Properties.Resources.app_icon;

            // set form title
            Text = Program.programName;

            // disable resizing the window
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;

            // position the plug-in in the lower right corner of the screen
            Rectangle workingArea = Screen.GetWorkingArea(this);
            Location = new Point(workingArea.Right - Size.Width - 130,
                                 workingArea.Bottom - Size.Height - 50);

            // always display on top
            TopMost = true;

            // --------------------------------------------------------------------------------------------------------

            // disable ui
            panelMain.Enabled = false;

            // set version label text
            toolStripStatusLabelVersion.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            // init estimated cycle time
            estimatedCycleTimeLabel.Text = "---.---";

            // --------------------------------------------------------------------------------------------------------

            // start the ready timer
            readyTimer.Interval = 250; // 250 ms interval
            readyTimer.Enabled = true;
            readyTimer.Start();

            // start the update timer
            updateTimer.Interval = 250; // 250 ms interval
            updateTimer.Enabled = true;
            updateTimer.Start();

            // --------------------------------------------------------------------------------------------------------
        }

        // ::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
        //
        // Timers
        //
        // ::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::

        private void readyTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // is vna ready?
                if (Program.vna.app.Ready)
                {
                    // yes... vna is ready
                    comConnectionState = ComConnectionStateEnum.CONNECTED_VNA_READY;
                }
                else
                {
                    // no... vna is not ready
                    comConnectionState = ComConnectionStateEnum.CONNECTED_VNA_NOT_READY;
                }
            }
            catch (COMException)
            {
                // com connection has been lost
                comConnectionState = ComConnectionStateEnum.NOT_CONNECTED;
                Application.Exit();
                return;
            }

            if (comConnectionState != previousComConnectionState)
            {
                previousComConnectionState = comConnectionState;

                switch (comConnectionState)
                {
                    default:
                    case ComConnectionStateEnum.NOT_CONNECTED:

                        // update vna info text box
                        toolStripStatusLabelVnaInfo.ForeColor = Color.White;
                        toolStripStatusLabelVnaInfo.BackColor = Color.Red;
                        toolStripStatusLabelSpacer.BackColor = toolStripStatusLabelVnaInfo.BackColor;
                        toolStripStatusLabelVnaInfo.Text = "VNA NOT CONNECTED";

                        // disable ui
                        panelMain.Enabled = false;

                        break;

                    case ComConnectionStateEnum.CONNECTED_VNA_NOT_READY:

                        // update vna info text box
                        toolStripStatusLabelVnaInfo.ForeColor = Color.White;
                        toolStripStatusLabelVnaInfo.BackColor = Color.Red;
                        toolStripStatusLabelSpacer.BackColor = toolStripStatusLabelVnaInfo.BackColor;
                        toolStripStatusLabelVnaInfo.Text = "VNA NOT READY";

                        // disable ui
                        panelMain.Enabled = false;

                        break;

                    case ComConnectionStateEnum.CONNECTED_VNA_READY:

                        // get vna info
                        Program.vna.PopulateInfo(Program.vna.app.NAME);

                        // update vna info text box
                        toolStripStatusLabelVnaInfo.ForeColor = SystemColors.ControlText;
                        toolStripStatusLabelVnaInfo.BackColor = SystemColors.Control;
                        toolStripStatusLabelSpacer.BackColor = toolStripStatusLabelVnaInfo.BackColor;
                        toolStripStatusLabelVnaInfo.Text = Program.vna.modelString + "   " + "SN:" + Program.vna.serialNumberString + "   " + Program.vna.versionString;

                        // enable ui
                        panelMain.Enabled = true;

                        break;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------

        private void updateTimer_Tick(object sender, EventArgs e)
        {
            if (comConnectionState == ComConnectionStateEnum.CONNECTED_VNA_READY)
            {
                updateEstimatedCycleTime();
            }
        }

        // ::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::

        private void updateEstimatedCycleTime()
        {
            // init error flag
            bool isError = false;

            // init estimated cycle time
            double estimatedCycleTime = 0.0;

            // parse the vna family type
            switch (Program.vna.family)
            {
                case VnaFamilyEnum.S2:
                    {
                        // determine the instrument model-dependent fixed measurement time (in seconds)
                        double t_fixed = determineFixedMeasurementTime(Program.vna.model);
                        if (t_fixed == -1)
                        {
                            isError = true;
                            break;
                        }

                        // determine number of channels
                        int numOfChannels;
                        try
                        {
                            long splitIndex = Program.vna.app.SCPI.DISPlay.SPLit;
                            numOfChannels = Program.vna.DetermineNumberOfChannels(splitIndex);
                        }
                        catch
                        {
                            break;
                        }

                        // loop thru all channels and determine the estimated duration of each
                        List<double> t_chList = new List<double>();
                        for (int ch = 1; ch < numOfChannels + 1; ch++)
                        {
                            string sweepType = "";
                            try
                            {
                                // determine the segment sweep mode for this channel
                                sweepType = Program.vna.app.SCPI.SENSe[ch].SWEep.TYPE;
                            }
                            catch
                            {
                                break;
                            }
                            bool isSegmentedSweepMode = determineSegmentSweepMode(sweepType);

                            long n_pts = 0; // the total number of measurement points
                            double t_ifbw = 0; // the bandwidth-dependent measurement time (in seconds)
                            double t_delay = 0; // the measurement delay (in seconds)

                            if (isSegmentedSweepMode == false)
                            {
                                double ifbw;
                                try
                                {
                                    // get total number of measurement points for this channel
                                    n_pts = Program.vna.app.SCPI.SENSe[ch].SWEep.POINts;

                                    // get if bandwidth setting (in hertz) for this channel
                                    ifbw = Program.vna.app.SCPI.SENSe[ch].BANDwidth.RESolution;

                                    // calculate total bandwidth-dependent measurement time (in seconds)
                                    t_ifbw = calculateBandwidthDependentMeasurementTime(ifbw);

                                    // get measurement delay setting (in seconds) for this channel
                                    t_delay = Program.vna.app.SCPI.SENSe[ch].SWEep.POINt.TIME;
                                }
                                catch
                                {
                                    break;
                                }
                            }
                            else
                            {
                                try
                                {
                                    // get segment data
                                    double[] segmentData = Program.vna.app.SCPI.SENSe[ch].SEGMent.DATA;

                                    // process segment data
                                    processSegmentData(segmentData, out n_pts, out t_ifbw, out t_delay);
                                }
                                catch
                                {
                                    break;
                                }
                            }

                            // calculate the estimated duration of this channel's sweep (in seconds)
                            double t_sweep = calculateEstimatedDurationOfChannelSweep(n_pts, t_fixed, t_ifbw, t_delay);

                            // determine number of sweeps for this channel...
                            // init number of sweeps to one
                            long n_sweeps = 1;

                            // get error correction state for this channel
                            bool isErrorCorrectionOn = false;
                            try
                            {
                                isErrorCorrectionOn = Program.vna.app.SCPI.SENSe[ch].CORRection.STATe;
                            }
                            catch
                            {
                                break;
                            }

                            // is error correction on?
                            if (isErrorCorrectionOn == true)
                            {
                                // yes...

                                long numOfTraces = 1;
                                try
                                {
                                    // get number of traces for this channel
                                    numOfTraces = Program.vna.app.SCPI.CALCulate[ch].PARameter.COUNt;
                                }
                                catch
                                {
                                    break;
                                }

                                // loop thru all traces on this channel
                                for (int tr = 1; tr < numOfTraces + 1; tr++)
                                {
                                    try
                                    {
                                        // get correction data for this trace
                                        object[] correctionData = Program.vna.app.SCPI.SENSe[ch].CORRection.TYPE[tr];

                                        // is calibration type = full 2–port calibration?
                                        if ((string)correctionData[0] == "SOLT2")
                                        {
                                            // yes... number of sweeps is two
                                            n_sweeps = 2;
                                            break;  // on to the next channel
                                        }
                                    }
                                    catch
                                    {
                                        break;
                                    }
                                }
                            }

                            // calculate estimated duration of this channel (in seconds)
                            double t_ch = calculateEstimatedDurationOfChannel(n_sweeps, t_sweep);

                            // add to the channel estimated duration list
                            t_chList.Add(t_ch);
                        }

                        // calculate usb latency
                        double t_usb = calculateUsbLatency(numOfChannels);

                        //  ===========================================================================================

                        // calculate estimated cycle time (in seconds)
                        //
                        //
                        // Estimated Cycle Time = t_usb + (t_ch1 + t_ch2… +t_chN )
                        //
                        //      where:
                        //          • t_usb = USB Latency (in Seconds) = Number of Channels * 0.005s
                        //          • t_chN = Estimated Duration of Channel N (in Seconds)

                        estimatedCycleTime = t_usb + t_chList.Sum();

                        //  ===========================================================================================
                    }
                    break;

                case VnaFamilyEnum.TR:
                    {
                        // determine the instrument model-dependent fixed measurement time (in seconds)
                        double t_fixed = determineFixedMeasurementTime(Program.vna.model);
                        if (t_fixed == -1)
                        {
                            isError = true;
                            break;
                        }

                        // determine number of channels
                        int numOfChannels;
                        try
                        {
                            long splitIndex = Program.vna.app.SCPI.DISPlay.SPLit;
                            numOfChannels = Program.vna.DetermineNumberOfChannels(splitIndex);
                        }
                        catch
                        {
                            break;
                        }

                        // loop thru all channels and determine the estimated duration of each
                        List<double> t_chList = new List<double>();
                        for (int ch = 1; ch < numOfChannels + 1; ch++)
                        {
                            // determine the segment sweep mode for this channel
                            string sweepType = "";
                            try
                            {
                                sweepType = Program.vna.app.SCPI.SENSe[ch].SWEep.TYPE;
                            }
                            catch
                            {
                                break;
                            }
                            bool isSegmentedSweepMode = determineSegmentSweepMode(sweepType);

                            long n_pts = 0; // the total number of measurement points
                            double t_ifbw = 0; // the bandwidth-dependent measurement time (in seconds)
                            double t_delay = 0; // the measurement delay (in seconds)

                            if (isSegmentedSweepMode == false)
                            {
                                try
                                {
                                    // get total number of measurement points for this channel
                                    n_pts = Program.vna.app.SCPI.SENSe[ch].SWEep.POINts;

                                    // get if bandwidth setting (in hertz) for this channel
                                    double ifbw = Program.vna.app.SCPI.SENSe[ch].BANDwidth.RESolution;

                                    // calculate total bandwidth-dependent measurement time (in seconds)
                                    t_ifbw = calculateBandwidthDependentMeasurementTime(ifbw);

                                    // get measurement delay setting (in seconds) for this channel
                                    t_delay = Program.vna.app.SCPI.SENSe[ch].SWEep.POINt.TIME;
                                }
                                catch
                                {
                                    break;
                                }
                            }
                            else
                            {
                                try
                                {
                                    // get segment data
                                    double[] segmentData = Program.vna.app.SCPI.SENSe[ch].SEGMent.DATA;

                                    // process segment data
                                    processSegmentData(segmentData, out n_pts, out t_ifbw, out t_delay);
                                }
                                catch
                                {
                                    break;
                                }
                            }

                            // calculate the estimated duration of this channel's sweep (in seconds)
                            double t_sweep = calculateEstimatedDurationOfChannelSweep(n_pts, t_fixed, t_ifbw, t_delay);

                            // determine number of sweeps for this channel
                            long n_sweeps = 1; // number of sweeps is always 1 for this family

                            // calculate estimated duration of this channel (in seconds)
                            double t_ch = calculateEstimatedDurationOfChannel(n_sweeps, t_sweep);

                            // add to the channel estimated duration list
                            t_chList.Add(t_ch);
                        }

                        // calculate usb latency
                        double t_usb = calculateUsbLatency(numOfChannels);

                        //  ===========================================================================================

                        // calculate estimated cycle time (in seconds)
                        //
                        //
                        // Estimated Cycle Time = t_usb + (t_ch1 + t_ch2… +t_chN )
                        //
                        //      where:
                        //          • t_usb = USB Latency (in Seconds) = Number of Channels * 0.005s
                        //          • t_chN = Estimated Duration of Channel N (in Seconds)

                        estimatedCycleTime = t_usb + t_chList.Sum();

                        //  ===========================================================================================
                    }
                    break;

                default:
                    isError = true;
                    break;
            }

            if (isError == true)
            {
                // display error message
                estimatedCycleTimeLabel.Text = "ERROR";
            }
            else
            {
                // round "up"
                estimatedCycleTime = Math.Round(estimatedCycleTime, 3, MidpointRounding.AwayFromZero);

                // display in label formatted to three decimal places
                estimatedCycleTimeLabel.Text = string.Format("{0:0.000}", estimatedCycleTime);
            }
        }

        // ::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::

        private double determineFixedMeasurementTime(VnaModelEnum model)
        {
            switch (model)
            {
                default:
                    return -1;

                // R family types
                case VnaModelEnum.R54:
                    return -1; // NOT SUPPORTED

                case VnaModelEnum.R140:
                    return -1; // NOT SUPPORTED

                // TR family types
                case VnaModelEnum.TR1300_1:
                    return 0.000111;

                case VnaModelEnum.TR5048:
                    return 0.000211;

                case VnaModelEnum.TR7530:
                    return 0.000211;

                // S2 family types
                case VnaModelEnum.C1209:
                    return 0.0000138;

                case VnaModelEnum.C1220:
                    return 0.00000882;

                case VnaModelEnum.S5048:
                    return 0.000211;

                case VnaModelEnum.S5070:
                    return 0.000211; // TODO: verify this model

                case VnaModelEnum.S7530:
                    return 0.000211;

                case VnaModelEnum.PLANAR_304_1:
                    return 0.0000807;

                case VnaModelEnum.PLANAR_804_1: // includes 814/1
                    return 0.000061;

                case VnaModelEnum.C4209:
                    return -1; // NOT SUPPORTED

                // S4 family types
                case VnaModelEnum.PLANAR_808_1:
                    return -1; // NOT SUPPORTED
            }
        }

        private bool determineSegmentSweepMode(string sweepType)
        {
            if (string.Compare(sweepType, "SEGM", true) == 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private double calculateBandwidthDependentMeasurementTime(double ifbw)
        {
            double time = 0;
            if (ifbw > 0)
            {
                // t_ifbw = 1.18 / IF bandwidth setting (in hertz)
                time = (1.18 / ifbw);
            }
            return time;
        }

        private void processSegmentData(double[] segmentData, out long n_pts, out double t_ifbw, out double t_delay)
        {
            n_pts = 0; // the total number of measurement points
            t_ifbw = 0; // the bandwidth-dependent measurement time (in seconds)
            t_delay = 0; // the measurement delay (in seconds)

            if (segmentData != null)
            {
                // read flags
                bool isIfbwEnabled = Convert.ToBoolean(segmentData[2]);
                bool isPowerEnabled = Convert.ToBoolean(segmentData[3]);
                bool isDelayEnabled = Convert.ToBoolean(segmentData[4]);

                // read number of segments
                double numOfSegments = segmentData[6];

                // loop thru all segments
                int segmentIndex = 6; // initial offset to get past flags
                for (int segment = 0; segment < numOfSegments; segment++)
                {
                    //  ===============================================================================================

                    // calculate the total number of measurement points
                    //
                    //      with segment sweep mode ENABLED
                    //          n_pts = n_pts1 + n_pts2 … n_ptsM
                    //
                    //      where:
                    //          • n_pts = the total number of measurement points
                    //          • n_ptsM = number of measurement points in segment m

                    // increment index to get this segment's number of measurement points
                    segmentIndex += 3;

                    // get this segment's number of measurement points
                    double pts = segmentData[segmentIndex];

                    // update total number of measurement points
                    n_pts += (long)pts;

                    //  ===============================================================================================

                    // calculate the bandwidth-dependent measurement time (in seconds)
                    //
                    //
                    //      with segment sweep mode ENABLED
                    //          t_ifbw = (n_pts1 * t_ifbw1 + n_pts2 * t_ifbw2 … +n_ptsM * t_ifbwM) / n_pts
                    //
                    //      where:
                    //          • t_ifbw = the bandwidth-dependent measurement time (in seconds)
                    //          • n_ptsM = number of measurement points in segment m
                    //          • t_ifbwM = 1.18 / IF bandwidth setting (in hertz) for segment m
                    //          • n_pts = total number of measurement points

                    if (isIfbwEnabled == true)
                    {
                        // increment index to get this segment's if bandwidth
                        segmentIndex += 1;

                        // get this segment's if bandwidth
                        double ifbw = segmentData[segmentIndex];

                        // update total bandwidth-dependent measurement time (in seconds)
                        t_ifbw = t_ifbw + (pts * calculateBandwidthDependentMeasurementTime(ifbw));
                    }

                    //  ===============================================================================================

                    if (isPowerEnabled == true)
                    {
                        // though not used increment index to account for this segment's power value
                        segmentIndex += 1;
                    }

                    //  ===============================================================================================
                    // calculate the measurement delay (in seconds)
                    //
                    //      with segment sweep mode ENABLED
                    //          t_delay = (n_pts1 * t_delay1 + n_pts2 * t_delay2 … +n_ptsM * t_delayM) / n_pts
                    //
                    //      where:
                    //          • t_delay = the measurement delay (in seconds)
                    //          • n_ptsM = number of measurement points in segment m
                    //          • t_delayM = measurement delay setting (in seconds) for segment m
                    //          • n_pts = total number of measurement points

                    if (isDelayEnabled == true)
                    {
                        // increment index to get this segment's measurement delay
                        segmentIndex += 1;

                        // get this segment's measurement delay
                        double delay = segmentData[segmentIndex];

                        // update total measurement delay (in seconds)
                        t_delay = t_delay + (pts * delay);
                    }

                    //  ===============================================================================================
                }

                if (n_pts > 0)
                {
                    // calculate average for bandwidth-dependent measurement time (in seconds)
                    if (isIfbwEnabled == true)
                    {
                        t_ifbw = t_ifbw / n_pts;
                    }

                    // calculate average for measurement delay (in seconds)
                    if (isDelayEnabled == true)
                    {
                        t_delay = t_delay / n_pts;
                    }
                }
            }
        }

        private double calculateEstimatedDurationOfChannelSweep(long n_pts, double t_fixed, double t_ifbw, double t_delay)
        {
            // calculate the estimated duration of this channel's sweep (in seconds)
            //
            //      t_sweep = n_pts * (t_fixed + t_ifbw + t_delay)
            //
            //      where:
            //          • t_sweep = the estimated duration of this channel's sweep (in seconds)
            //          • n_pts = number of measurement points
            //          • t_fixed = instrument model-dependent fixed measurement time (in seconds)
            //          • t_ifbw = bandwidth-dependent measurement time (in seconds)
            //          • t_delay = measurement delay (in seconds)

            double t_sweep = n_pts * (t_fixed + t_ifbw + t_delay);
            return t_sweep;
        }

        private double calculateEstimatedDurationOfChannel(long n_sweeps, double t_sweep)
        {
            // calculate estimated duration of this channel (in seconds)
            //
            //      t_ch = n_sweeps * t_sweep
            //
            //      where:
            //          • t_ch = estimated duration of this channel (in seconds)
            //          • n_sweeps = number of sweeps for this channel
            //          • t_sweep = estimated duration of this channel's sweep (in seconds)

            double t_ch = (n_sweeps * t_sweep);
            return t_ch;
        }

        private double calculateUsbLatency(int numOfChannels)
        {
            // calculate usb latency (in seconds)
            //
            //      t_usb = number of channels * 0.005s
            //
            //      where:
            //          • t_usb = usb latency (in Seconds)

            return (numOfChannels * 0.005);
        }

        // ::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
    }
}