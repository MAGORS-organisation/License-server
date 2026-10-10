using System.Runtime.InteropServices;

namespace Achilles.Crypto.Kms;

/// <summary>
/// Native interop structures, constants, and function pointer delegates
/// conforming to OASIS PKCS #11 Cryptographic Token Interface Base Specification v3.2.
/// </summary>
internal static class Pkcs11Native
{
    // =========================================================================
    // Return Values (CK_RV)
    // =========================================================================
    public const ulong CKR_OK = 0x00000000;
    public const ulong CKR_CANCEL = 0x00000001;
    public const ulong CKR_HOST_MEMORY = 0x00000002;
    public const ulong CKR_SLOT_ID_INVALID = 0x00000003;
    public const ulong CKR_GENERAL_ERROR = 0x00000005;
    public const ulong CKR_FUNCTION_FAILED = 0x00000006;
    public const ulong CKR_ARGUMENTS_BAD = 0x00000007;
    public const ulong CKR_NO_EVENT = 0x00000008;
    public const ulong CKR_NEED_TO_CREATE_THREADS = 0x00000009;
    public const ulong CKR_CANT_LOCK = 0x0000000A;
    public const ulong CKR_TOKEN_NOT_PRESENT = 0x000000E0;
    public const ulong CKR_TOKEN_NOT_RECOGNIZED = 0x000000E1;
    public const ulong CKR_TOKEN_WRITE_PROTECTED = 0x000000E2;
    public const ulong CKR_SESSION_HANDLE_INVALID = 0x000000B3;
    public const ulong CKR_SESSION_PARALLEL_NOT_SUPPORTED = 0x000000B4;
    public const ulong CKR_SESSION_READ_ONLY = 0x000000B5;
    public const ulong CKR_SESSION_EXISTS = 0x000000B6;
    public const ulong CKR_SESSION_READ_ONLY_EXISTS = 0x000000B7;
    public const ulong CKR_SESSION_READ_WRITE_SO_EXISTS = 0x000000B8;
    public const ulong CKR_PIN_INCORRECT = 0x000000A0;
    public const ulong CKR_PIN_INVALID = 0x000000A1;
    public const ulong CKR_PIN_LEN_RANGE = 0x000000A2;
    public const ulong CKR_PIN_EXPIRED = 0x000000A3;
    public const ulong CKR_PIN_LOCKED = 0x000000A4;
    public const ulong CKR_OBJECT_HANDLE_INVALID = 0x00000082;
    public const ulong CKR_MECHANISM_INVALID = 0x00000070;
    public const ulong CKR_MECHANISM_PARAM_INVALID = 0x00000071;
    public const ulong CKR_BUFFER_TOO_SMALL = 0x00000150;
    public const ulong CKR_CRYPTOKI_NOT_INITIALIZED = 0x00000190;
    public const ulong CKR_CRYPTOKI_ALREADY_INITIALIZED = 0x00000191;

    // =========================================================================
    // Object Classes and Key Types
    // =========================================================================
    public const ulong CKO_DATA = 0x00000000;
    public const ulong CKO_CERTIFICATE = 0x00000001;
    public const ulong CKO_PUBLIC_KEY = 0x00000002;
    public const ulong CKO_PRIVATE_KEY = 0x00000003;
    public const ulong CKO_SECRET_KEY = 0x00000004;

    public const ulong CKK_RSA = 0x00000000;
    public const ulong CKK_DSA = 0x00000001;
    public const ulong CKK_DH = 0x00000002;
    public const ulong CKK_EC = 0x00000003;
    public const ulong CKK_GENERIC_SECRET = 0x00000010;

    // Post-Quantum Key Types (PKCS#11 v3.2 Draft & Vendor extensions)
    public const ulong CKK_ML_DSA = 0x00000040;
    public const ulong CKK_ML_KEM = 0x00000041;
    public const ulong CKK_SLH_DSA = 0x00000042;

    // =========================================================================
    // Attributes (CKA_)
    // =========================================================================
    public const ulong CKA_CLASS = 0x00000000;
    public const ulong CKA_TOKEN = 0x00000001;
    public const ulong CKA_PRIVATE = 0x00000002;
    public const ulong CKA_LABEL = 0x00000003;
    public const ulong CKA_APPLICATION = 0x00000010;
    public const ulong CKA_VALUE = 0x00000011;
    public const ulong CKA_OBJECT_ID = 0x00000012;
    public const ulong CKA_CERTIFICATE_TYPE = 0x00000080;
    public const ulong CKA_ISSUER = 0x00000081;
    public const ulong CKA_SERIAL_NUMBER = 0x00000082;
    public const ulong CKA_KEY_TYPE = 0x00000100;
    public const ulong CKA_SUBJECT = 0x00000101;
    public const ulong CKA_ID = 0x00000102;
    public const ulong CKA_SENSITIVE = 0x00000103;
    public const ulong CKA_ENCRYPT = 0x00000104;
    public const ulong CKA_DECRYPT = 0x00000105;
    public const ulong CKA_WRAP = 0x00000106;
    public const ulong CKA_UNWRAP = 0x00000107;
    public const ulong CKA_SIGN = 0x00000108;
    public const ulong CKA_SIGN_RECOVER = 0x00000109;
    public const ulong CKA_VERIFY = 0x0000010A;
    public const ulong CKA_VERIFY_RECOVER = 0x0000010B;
    public const ulong CKA_DERIVE = 0x0000010C;
    public const ulong CKA_EC_PARAMS = 0x00000180;
    public const ulong CKA_EC_POINT = 0x00000181;
    public const ulong CKA_ALWAYS_AUTHENTICATE = 0x00000202;

    // =========================================================================
    // Mechanisms (CKM_)
    // =========================================================================
    public const ulong CKM_ECDSA = 0x00001041;
    public const ulong CKM_ECDSA_SHA256 = 0x00001042;
    public const ulong CKM_ECDSA_SHA384 = 0x00001043;
    public const ulong CKM_ECDSA_SHA512 = 0x00001044;
    public const ulong CKM_ML_DSA_44 = 0x00002044;
    public const ulong CKM_ML_DSA_65 = 0x00002065;
    public const ulong CKM_ML_DSA_87 = 0x00002087;

    // =========================================================================
    // Session and User Flags
    // =========================================================================
    public const ulong CKF_SERIAL_SESSION = 0x00000004;
    public const ulong CKF_RW_SESSION = 0x00000002;
    public const ulong CKU_SO = 0x00000000;
    public const ulong CKU_USER = 0x00000001;
    public const ulong CKU_CONTEXT_SPECIFIC = 0x00000002;

    // =========================================================================
    // Cryptoki Data Structures
    // =========================================================================
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CK_VERSION
    {
        public byte major;
        public byte minor;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CK_INFO
    {
        public CK_VERSION cryptokiVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] manufacturerID;
        public ulong flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] libraryDescription;
        public CK_VERSION libraryVersion;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CK_SLOT_INFO
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] slotDescription;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] manufacturerID;
        public ulong flags;
        public CK_VERSION hardwareVersion;
        public CK_VERSION firmwareVersion;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CK_TOKEN_INFO
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] label;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] manufacturerID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] model;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] serialNumber;
        public ulong flags;
        public ulong ulMaxSessionCount;
        public ulong ulSessionCount;
        public ulong ulMaxRwSessionCount;
        public ulong ulRwSessionCount;
        public ulong ulMaxPinLen;
        public ulong ulMinPinLen;
        public ulong ulTotalPublicMemory;
        public ulong ulFreePublicMemory;
        public ulong ulTotalPrivateMemory;
        public ulong ulFreePrivateMemory;
        public CK_VERSION hardwareVersion;
        public CK_VERSION firmwareVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] utcTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CK_MECHANISM
    {
        public ulong mechanism;
        public nint pParameter;
        public ulong ulParameterLen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CK_ATTRIBUTE
    {
        public ulong type;
        public nint pValue;
        public ulong ulValueLen;
    }

    // =========================================================================
    // Function Pointer Delegates
    // =========================================================================
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_InitializeDelegate(nint pInitArgs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_FinalizeDelegate(nint pReserved);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_GetInfoDelegate(ref CK_INFO pInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_GetSlotListDelegate(byte tokenPresent, [Out] ulong[]? pSlotList, ref ulong pulCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_GetTokenInfoDelegate(ulong slotID, ref CK_TOKEN_INFO pInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_OpenSessionDelegate(ulong slotID, ulong flags, nint pApplication, nint Notify, ref ulong phSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_CloseSessionDelegate(ulong hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_LoginDelegate(ulong hSession, ulong userType, [In] byte[]? pPin, ulong ulPinLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_LogoutDelegate(ulong hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_FindObjectsInitDelegate(ulong hSession, [In] CK_ATTRIBUTE[] pTemplate, ulong ulCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_FindObjectsDelegate(ulong hSession, [Out] ulong[] phObject, ulong ulMaxObjectCount, ref ulong pulObjectCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_FindObjectsFinalDelegate(ulong hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_GetAttributeValueDelegate(ulong hSession, ulong hObject, [In, Out] CK_ATTRIBUTE[] pTemplate, ulong ulCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_SignInitDelegate(ulong hSession, ref CK_MECHANISM pMechanism, ulong hKey);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong C_SignDelegate(ulong hSession, [In] byte[] pData, ulong ulDataLen, [Out] byte[]? pSignature, ref ulong pulSignatureLen);
}
