#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GamingControlCenter;

public class VariableMonitor
{
	private readonly Queue<DateTime> _timestampQueue = new Queue<DateTime>();

	private readonly TimeSpan _window = TimeSpan.FromSeconds(60L);

	private readonly int _requiredCount = 10;

	private CancellationTokenSource _cts;

	public async Task StartMonitoringAsync(Func<int> dynamicValue)
	{
		if (_cts != null && !_cts.IsCancellationRequested)
		{
			Trace.WriteLine("监控已在运行");
			return;
		}
		_cts = new CancellationTokenSource();
		CancellationToken token = _cts.Token;
		await Task.Run(async () =>
		{
			try
			{
				while (!token.IsCancellationRequested)
				{
					if (dynamicValue() > 26)
					{
						lock (_timestampQueue)
						{
							_timestampQueue.Enqueue(DateTime.Now);
							while (_timestampQueue.Count > 0 && DateTime.Now - _timestampQueue.Peek() > _window)
							{
								_timestampQueue.Dequeue();
							}
						}
					}
					await Task.Delay(1000, token);
				}
			}
			catch (OperationCanceledException)
			{
				Trace.WriteLine("监控任务被取消");
			}
		}, token);
	}

	public async Task StopMonitoringAsync()
	{
		if (_cts == null || _cts.IsCancellationRequested)
		{
			Trace.WriteLine("监控未在运行");
			return;
		}
		_cts.Cancel();
		await Task.CompletedTask;
		_cts.Dispose();
		_cts = null;
	}

	public bool IsConditionMet()
	{
		lock (_timestampQueue)
		{
			while (_timestampQueue.Count > 0 && DateTime.Now - _timestampQueue.Peek() > _window)
			{
				_timestampQueue.Dequeue();
			}
			return _timestampQueue.Count >= _requiredCount;
		}
	}
}
