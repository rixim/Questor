﻿//------------------------------------------------------------------------------
//  <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//    Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//    Please look in the accompanying license.htm file for the license that 
//    applies to this source code. (a copy can also be found at: 
//    http://www.thehackerwithin.com/license.htm)
//  </copyright>
//-------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Xml.XPath;
using System.Reflection;
using System.Xml.Linq;
using System.IO;
using System.Timers;
using Mono.Options;

namespace Questor
{
    using global::Questor.Modules;
    using LavishScriptAPI;
    using DirectEve;

    static class Program
    {
        private static bool _done;
        private static DirectEve _directEve;
        public static List<CharSchedule> CharSchedules { get; private set; }
        private static int _pulsedelay = 10;

        private static string _username;
        private static string _password;
        private static string _character;
        private static string _scriptFile;
        private static bool   _loginOnly;
        private static bool   _showHelp;
        private static bool _chantlingScheduler;

        private static DateTime _startTime;
        public static DateTime _stopTime;
        private static double minutesToStart;
        private static bool _readyToStarta;
        private static bool _readyToStart;

        static System.Timers.Timer _timer = new System.Timers.Timer();
        private static int _randStartDelay = 30;
        private static Random _r = new Random();
        public static bool stopTimeSpecified = false;

        private static DateTime _lastPulse;
        private static DateTime _startTime;

        public static DateTime startTime
        {
           get 
           {
              return _startTime; 
           }
        }

        public static int maxRuntime
        {
            get
            {
                return _maxRuntime;
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            _maxRuntime = Int32.MaxValue;
            var p = new OptionSet() {
                "Usage: questor [OPTIONS]",
                "Run missions and make uber ISK.",
                "",
                "Options:",
                { "u|user=", "the {USER} we are logging in as.",
                v => _username = v },
                { "p|password=", "the user's {PASSWORD}.",
                v => _password = v },
                { "c|character=", "the {CHARACTER} to use.",
                v => _character = v },
                { "s|script=", "a {SCRIPT} file to execute before login.",
                v => _scriptFile = v },
                { "l|login", "login only and exit.",
                v => _loginOnly = v != null },
                { "x|chantling", "use chantling's scheduler",
                v => _chantlingScheduler = v != null },
                { "h|help", "show this message and exit",
                v => _showHelp = v != null },
                };

            List<string> extra;
            try
            {
                extra = p.Parse(args);
                //Logging.Log(string.Format("questor: extra = {0}", string.Join(" ", extra.ToArray())));
            }
            catch (OptionException e)
            {
                Logging.Log("questor: ");
                Logging.Log(e.Message);
                Logging.Log("Try `questor --help' for more information.");
                return;
            }
                _readyToStart = true;

            if (_showHelp)
            {
                System.IO.StringWriter sw = new System.IO.StringWriter();
                p.WriteOptionDescriptions(sw);
                Logging.Log(sw.ToString());
                return;
            }

            if (_chantlingScheduler && string.IsNullOrEmpty(_character))
            {
                Logging.Log("Error: to use chantling's scheduler, you also need to provide a character name!");
                return;
            }

            if (_chantlingScheduler && !string.IsNullOrEmpty(_character))
            {
                var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _character = _character.Replace("\"", "");  // strip quotation marks if any are present

                CharSchedules = new List<CharSchedule>();
                var values = XDocument.Load(Path.Combine(path, "Schedules.xml"));
                foreach (var value in values.Root.Elements("char"))
                    CharSchedules.Add(new CharSchedule(value));

                var _schedule = CharSchedules.FirstOrDefault(v => v.Name == _character);
                if (_schedule == null)
                {
                    Logging.Log("[Startup] Error - character not found!");
                    return;
                }
                else
                {
                    Logging.Log("[Startup] User: " + _schedule.User + " PW: " + _schedule.PW + " Name: " + _schedule.Name
                        + " Start: " + _schedule.Start + " Stop: " + _schedule.Stop + " RunTime: " + _schedule.RunTime);
                    if (_schedule.User == null || _schedule.PW == null)
                    {
                        Logging.Log("[Startup] Error - Login details not specified in Schedules.xml!");
                        return;
                    }
                    else
                    {
                        _username = _schedule.User;
                        _password = _schedule.PW;
                    }
                    _startTime = _schedule.Start;
                    if (_schedule.startTimeSpecified)
                        _startTime = _startTime.AddSeconds((double)(_r.Next(0, (_randStartDelay * 60))));
                    _stopTime = _schedule.Stop;

                    if ((DateTime.Now > _startTime))
                    {
                        if ((DateTime.Now.Subtract(_startTime).TotalMinutes < 720)) //if we're less than 12 hours past start time, start now
                        {
                            _startTime = DateTime.Now;
                            _readyToStarta = true;
                        }
                        else
                            _startTime = _startTime.AddDays(1); //otherwise, start tomorrow at start time
                    }
                    else
                        if ((_startTime.Subtract(DateTime.Now).TotalMinutes > 720)) //if we're more than 12 hours shy of start time, start now
                        {
                            _startTime = DateTime.Now;
                            _readyToStarta = true;
                        }
                    if (_stopTime < _startTime)
                        _stopTime = _stopTime.AddDays(1);

                    if (_schedule.RunTime > 0) //if runtime is specified, overrides stop time
                        _stopTime = _startTime.AddHours(_schedule.RunTime);

                    string _stopTimeText = "No stop time specified";
                    stopTimeSpecified = _schedule.stopTimeSpecified;
                    if (stopTimeSpecified)
                        _stopTimeText = _stopTime.ToString();

                    Logging.Log("[Startup] Start Time: " + _startTime + " - Stop Time: " + _stopTimeText);

                    if (!_readyToStarta)
                    {
                        minutesToStart = _startTime.Subtract(DateTime.Now).TotalMinutes;
                        Logging.Log("[Startup] Starting at " + _startTime + ". " + String.Format("{0:0.##}", minutesToStart) + " minutes to go.");
                        _timer.Elapsed += new ElapsedEventHandler(TimerEventProcessor);
                        if (minutesToStart > 0)
                            _timer.Interval = (int)(minutesToStart * 60000);
                        else
                            _timer.Interval = 1000;
                        _timer.Enabled = true;
                        _timer.Start();
                    }
                    else
                    {
                        _readyToStart = true;
                        Logging.Log("[Startup] Already passed start time.  Starting now.");
                    }
                }

                _directEve = new DirectEve();
                _directEve.OnFrame += OnFrame;

                while (!_done)
                {
                    System.Threading.Thread.Sleep(50);
                }

                _directEve.Dispose();
            }

            if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password) && !string.IsNullOrEmpty(_character))
            {
                _directEve = new DirectEve();
                _directEve.OnFrame += OnFrame;

                var started = DateTime.Now;

                // Sleep until we're done
                while (!_done)
                {
                    System.Threading.Thread.Sleep(50);
                }

                _directEve.Dispose();

                // If the last parameter is false, then we only auto-login
                if (_loginOnly)
                    return;
            }

            _startTime = DateTime.Now;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new frmMain());
        }

        static void OnFrame(object sender, EventArgs e)
        {
            if (!_readyToStart)
                return;

            if (_chantlingScheduler && !string.IsNullOrEmpty(_character) && !_readyToStarta)
                return;

            if (DateTime.Now.Subtract(_lastPulse).TotalSeconds < _pulsedelay)
                return;

            _lastPulse = DateTime.Now;

            // If the session is ready, then we are done :)
            if (_directEve.Session.IsReady)
            {
                Logging.Log("[Startup] We've successfully logged in");
                _done = true;
                return;
            }

            // We shouldn't get any window
            if (_directEve.Windows.Count != 0)
            {
                foreach(var window in _directEve.Windows)
                {
                    if (window.Name == "telecom")
                    {
                        Logging.Log("Questor: Closing telecom message...");
                        Logging.Log("Questor: Content of telecom window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]");
                        window.Close();
                        continue;
                    }

                    // Modal windows must be closed
                    // But lets only close known modal windows
                    if (window.Name == "modal")
                    {
                        bool close = false;
                        if (!string.IsNullOrEmpty(window.Html))
                        {
                            // Server going down
                            close |= window.Html.Contains("Please make sure your characters are out of harms way");
                            close |= window.Html.Contains("The socket was closed");
                            close |= window.Html.Contains("accepting connections");
                            close |= window.Html.Contains("Could not connect");
                            close |= window.Html.Contains("The specified proxy or server node");
                            close |= window.Html.Contains("Starting up");
                            close |= window.Html.Contains("Unable to connect to the selected server");
                            close |= window.Html.Contains("Could not connect to the specified address");
                            close |= window.Html.Contains("Connection Timeout");
                            close |= window.Html.Contains("The cluster is not currently accepting connections");
                            close |= window.Html.Contains("Your character is located within");
                            close |= window.Html.Contains("The transport has not yet been connected");
                            close |= window.Html.Contains("The user's connection has been usurped");
                            close |= window.Html.Contains("The EVE cluster has reached its maximum user limit");
                            close |= window.Html.Contains("The connection to the server was closed");
                            close |= window.Html.Contains("The client's local session information is corrupt");
                            _pulsedelay = 60;
                        }

                        if (close)
                        {
                            Logging.Log("Questor: Closing modal window...");
                            Logging.Log("Questor: Content of modal window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]");
                            window.Close();
                            continue;
                        }
                    }

                    if (string.IsNullOrEmpty(window.Html))
                        continue;

                    Logging.Log("[Startup] We've got an unexpected window, auto login halted.");
                    _done = true;
                    return;
                }
                return;
            }

            if (!string.IsNullOrEmpty(_scriptFile))
            {
                try
                {
                    // Replace this try block with the following once new DirectEve is pushed
                    // _directEve.RunScript(_scriptFile);

                    System.Reflection.MethodInfo info = _directEve.GetType().GetMethod("RunScript");

                    if (info == null)
                    {
                        Logging.Log("DirectEve.RunScript() doesn't exist.  Upgrade DirectEve.dll!");
                    }
                    else
                    {
                        Logging.Log(string.Format("Running {0}...", _scriptFile));
                        info.Invoke(_directEve, new Object[] { _scriptFile });
                    }
                }
                catch (System.Exception ex)
                {
                    Logging.Log(string.Format("Exception {0}...", ex.ToString()));
                    _done = true;
                }
                finally
                {
                    _scriptFile = null;
                }
                return;
            }

            if (_directEve.Login.AtLogin)
            {
                Logging.Log("[Startup] Login account [" + _username + "]");
                _directEve.Login.Login(_username, _password);
                _pulsedelay = 20;
                return;
            }

            if (_directEve.Login.AtCharacterSelection && _directEve.Login.IsCharacterSelectionReady)
            {
                foreach (var slot in _directEve.Login.CharacterSlots)
                {
                    if (slot.CharId.ToString() != _character && string.Compare(slot.CharName, _character, true) != 0)
                        continue;

                    Logging.Log("[Startup] Activating character [" + slot.CharName + "]");
                    slot.Activate();
                    return;
                }

                Logging.Log("[Startup] Character id/name [" + _character + "] not found, retrying in 10 seconds");
            }
        }

        static void TimerEventProcessor(Object myObject, EventArgs myEventArgs)
        {
            _timer.Stop();
            Logging.Log("[Startup] Timer elapsed.  Starting now.");
            _readyToStart = true;
            _readyToStarta = true;
        }
    
    }
}
