#include "Driver.h"
#include "DsIdentification.tmh"

BOOLEAN
DsIdentification_Parse(
	_In_reads_(ReportLength) const UCHAR* Report,
	_In_ SIZE_T ReportLength,
	_Out_ PDS_IDENTIFICATION_INFO Info
)
{
	UCHAR fieldCount;
	SIZE_T fieldEnd;
	BOOLEAN hasHwCal = FALSE;
	BOOLEAN plainZero = FALSE;
	UCHAR i;

	if (Report == NULL || Info == NULL)
	{
		return FALSE;
	}

	RtlZeroMemory(Info, sizeof(*Info));

	if (ReportLength < DS_IDENTIFICATION_MIN_PARSE_SIZE)
	{
		return FALSE;
	}

	fieldCount = Report[DS_IDENTIFICATION_FIELD_COUNT_OFFSET];
	if (fieldCount == 0 || fieldCount > DS_IDENTIFICATION_MAX_FIELDS)
	{
		return FALSE;
	}

	fieldEnd = (SIZE_T)DS_IDENTIFICATION_FIELD_LIST_OFFSET + fieldCount;
	if (fieldEnd > ReportLength)
	{
		return FALSE;
	}

	Info->Firmware[0] = Report[2];
	Info->Firmware[1] = Report[3];
	Info->Firmware[2] = Report[4];
	Info->FirmwarePacked =
		((UINT32)Report[2] << 16) |
		((UINT32)Report[3] << 8) |
		(UINT32)Report[4];
	Info->PadType = Report[8];
	Info->FieldCount = fieldCount;

	for (i = 0; i < fieldCount; i++)
	{
		Info->Fields[i] = Report[DS_IDENTIFICATION_FIELD_LIST_OFFSET + i];
		if (Info->Fields[i] == 0x07)
		{
			hasHwCal = TRUE;
		}
	}

	if (fieldCount >= 2 &&
		Info->Fields[0] == 0x01 &&
		Info->Fields[1] == 0x02)
	{
		plainZero = TRUE;
	}
	else if (fieldCount >= 3 &&
		Info->Fields[1] == 0x01 &&
		Info->Fields[2] == 0x02)
	{
		plainZero = TRUE;
	}

	if (hasHwCal)
	{
		Info->MotionPath = DsIdentificationMotionPathHwCal;
	}
	else if (!plainZero)
	{
		Info->MotionPath = DsIdentificationMotionPathSixaxis;
	}
	else
	{
		Info->MotionPath = DsIdentificationMotionPathPlainZero;
	}

	if (fieldCount == 2 &&
		Info->Fields[0] == 0x01 &&
		Info->Fields[1] == 0x02 &&
		Report[DS_IDENTIFICATION_CLONE_BYTE_OFFSET] == 0x64)
	{
		Info->CloneHeuristic = TRUE;
	}

	return TRUE;
}

VOID
DsIdentification_AssignDeviceProperties(
	_In_ WDFDEVICE Device,
	_In_ PDS_IDENTIFICATION_INFO Info
)
{
	WDF_DEVICE_PROPERTY_DATA propertyData;
	UCHAR padType;
	UCHAR motionPath;
	BOOLEAN cloneHeuristic;

	if (Info == NULL)
	{
		return;
	}

	WDF_DEVICE_PROPERTY_DATA_INIT(&propertyData, &DEVPKEY_DsHidMini_RO_IdentificationFirmware);
	propertyData.Flags |= PLUGPLAY_PROPERTY_PERSISTENT;
	propertyData.Lcid = LOCALE_NEUTRAL;

	(void)WdfDeviceAssignProperty(
		Device,
		&propertyData,
		DEVPROP_TYPE_UINT32,
		sizeof(UINT32),
		&Info->FirmwarePacked
	);

	padType = Info->PadType;
	WDF_DEVICE_PROPERTY_DATA_INIT(&propertyData, &DEVPKEY_DsHidMini_RO_IdentificationPadType);
	propertyData.Flags |= PLUGPLAY_PROPERTY_PERSISTENT;
	propertyData.Lcid = LOCALE_NEUTRAL;

	(void)WdfDeviceAssignProperty(
		Device,
		&propertyData,
		DEVPROP_TYPE_BYTE,
		sizeof(UCHAR),
		&padType
	);

	motionPath = (UCHAR)Info->MotionPath;
	WDF_DEVICE_PROPERTY_DATA_INIT(&propertyData, &DEVPKEY_DsHidMini_RO_IdentificationMotionPath);
	propertyData.Flags |= PLUGPLAY_PROPERTY_PERSISTENT;
	propertyData.Lcid = LOCALE_NEUTRAL;

	(void)WdfDeviceAssignProperty(
		Device,
		&propertyData,
		DEVPROP_TYPE_BYTE,
		sizeof(UCHAR),
		&motionPath
	);

	cloneHeuristic = Info->CloneHeuristic;
	WDF_DEVICE_PROPERTY_DATA_INIT(&propertyData, &DEVPKEY_DsHidMini_RO_IdentificationCloneHeuristic);
	propertyData.Flags |= PLUGPLAY_PROPERTY_PERSISTENT;
	propertyData.Lcid = LOCALE_NEUTRAL;

	(void)WdfDeviceAssignProperty(
		Device,
		&propertyData,
		DEVPROP_TYPE_BOOLEAN,
		sizeof(BOOLEAN),
		&cloneHeuristic
	);
}
