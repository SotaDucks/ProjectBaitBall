using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;
using UnityEngine.SceneManagement;

namespace FishFlock
{
    public class ThreadManager : MonoBehaviour
    {
        static GameObject instance;
        static ThreadManager singleton;

        readonly List<ThreadProcess> processes = new List<ThreadProcess>();

        static void SetupSingleton()
        {
            if (singleton != null) return;
            instance = new GameObject("FishFlockThreadManager");
            instance.hideFlags = HideFlags.HideAndDontSave;

            singleton = instance.AddComponent<ThreadManager>();

            ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);
            ThreadPool.SetMaxThreads(Environment.ProcessorCount, Environment.ProcessorCount);
        }

        void Start()
        {
            SceneManager.sceneUnloaded += OnSceneIsUnloaded;
        }

        void Update()
        {
            lock (processes)
            {
                for (int i = 0; i < processes.Count; i++)
                {
                    var process = processes[i];

                    if (process.AutoEvt != null)
                    {
                        process.AutoEvt.Set();

                    }
                }
            }
        }

        private void OnDestroy()
        {
            Reset();
        }

        private void OnApplicationQuit()
        {
            Reset();
        }

        void OnSceneIsUnloaded(Scene arg0)
        {
            Reset();
        }

        void Reset()
        {
            lock (processes)
            {
                foreach (ThreadProcess p in processes)
                {
                    p.Stop();
                }
                processes.Clear();
            }
        }

        public static ThreadProcess DoTask(Action callback)
        {
            SetupSingleton();
            ThreadProcess process = new ThreadProcess(callback);
            lock (singleton.processes) { singleton.processes.Add(process); }

            return process;
        }

        public static void EndTask(ThreadProcess process)
        {
            process.Stop();
            lock (singleton.processes)
                singleton.processes.Remove(process);
        }
    }

    public class ThreadProcess
    {
        bool running = true;
        Action callback;
        public AutoResetEvent AutoEvt { get; private set; }

        internal ThreadProcess(Action _callback)
        {
            callback = _callback;
            running = true;

            AutoEvt = new AutoResetEvent(true);

            WaitCallback waitCallback;
            waitCallback = new WaitCallback((p) => DoRun());

            ThreadPool.QueueUserWorkItem(waitCallback);
        }

        void DoRun()
        {
            while (running)
            {
                callback(); 
                AutoEvt.WaitOne(100);
                
            }
        }
        public void Stop() { running = false; }
    }
}
