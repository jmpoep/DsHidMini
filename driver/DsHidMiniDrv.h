#pragma once

VOID FORCEINLINE REVERSE_BYTE_ARRAY(PUCHAR start, int size)
{
	PUCHAR lo = start;
	PUCHAR hi = start + size - 1;
	UCHAR swap;
	while (lo < hi)
	{
		swap = *lo;
		*lo++ = *hi;
		*hi-- = swap;
	}
}

EVT_VirtualHidMini_InputReportProcess DsHidMini_RetrieveNextInputReport;

_Must_inspect_result_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
DSHM_SendOutputReport(
	_In_ PDEVICE_CONTEXT Context,
	_In_ DS_OUTPUT_REPORT_SOURCE Source
);

//
// Same as DSHM_SendOutputReport, but assumes Context->OutputReport.Lock is
// already held by the caller. Used by callers that must mutate the output
// report buffer and send it without releasing the lock in between (see
// DsLed.c and DsBth.Timers.c), so a concurrent writer can never be copied
// into the wire buffer mid-update.
// 
_Must_inspect_result_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
DSHM_SendOutputReportUnlocked(
	_In_ PDEVICE_CONTEXT Context,
	_In_ DS_OUTPUT_REPORT_SOURCE Source
);
