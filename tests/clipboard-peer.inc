// Protocol peer for the official FreeRDP sample server. Synthetic data only.
// Every local offer is read and validated before the peer offers its own text/files.
static const BYTE peer_text[]={'W',0,'I',0,'R',0,'E',0,0,0};
static const BYTE peer_local_text[]={'L',0,'O',0,'C',0,'A',0,'L',0,0,0};
static const BYTE peer_file[]="wire file bytes";
static UINT peer_offer(CliprdrServerContext* c,BOOL files) {
    CLIPRDR_FORMAT f={.formatId=files?0xc123:13,.formatName=files?"FileGroupDescriptorW":NULL};
    CLIPRDR_FORMAT_LIST list={.common.msgType=CB_FORMAT_LIST,.numFormats=1,.formats=&f};
    return c->ServerFormatList(c,&list);
}
static UINT peer_list(CliprdrServerContext* c,const CLIPRDR_FORMAT_LIST* list) {
    CLIPRDR_FORMAT_LIST_RESPONSE ack={.common={.msgType=CB_FORMAT_LIST_RESPONSE,.msgFlags=CB_RESPONSE_OK}};
    UINT rc=c->ServerFormatListResponse(c,&ack);
    if (rc || !list->numFormats) return rc;
    CLIPRDR_FORMAT_DATA_REQUEST request={.common={.msgType=CB_FORMAT_DATA_REQUEST,.dataLen=4},.requestedFormatId=list->formats[0].formatId};
    c->custom=(void*)(uintptr_t)request.requestedFormatId;
    return c->ServerFormatDataRequest(c,&request);
}
static UINT peer_data(CliprdrServerContext* c,const CLIPRDR_FORMAT_DATA_RESPONSE* data) {
    if (!(data->common.msgFlags&CB_RESPONSE_OK)) return ERROR_INVALID_DATA;
    if ((uintptr_t)c->custom==13) {
        if (data->common.dataLen!=sizeof(peer_local_text) || memcmp(data->requestedFormatData,peer_local_text,sizeof(peer_local_text))) return ERROR_INVALID_DATA;
        return peer_offer(c,FALSE);
    }
    if (data->common.dataLen!=596) return ERROR_INVALID_DATA;
    CLIPRDR_FILE_CONTENTS_REQUEST req={.common.msgType=CB_FILECONTENTS_REQUEST,.streamId=9,.listIndex=0,.dwFlags=FILECONTENTS_RANGE,.cbRequested=sizeof(peer_file)-1};
    return c->ServerFileContentsRequest(c,&req);
}
static UINT peer_file_response(CliprdrServerContext* c,const CLIPRDR_FILE_CONTENTS_RESPONSE* data) {
    if (!(data->common.msgFlags&CB_RESPONSE_OK) || data->cbRequested!=sizeof(peer_file)-1 || memcmp(data->requestedData,peer_file,sizeof(peer_file)-1)) return ERROR_INVALID_DATA;
    return peer_offer(c,TRUE);
}
static UINT peer_request(CliprdrServerContext* c,const CLIPRDR_FORMAT_DATA_REQUEST* req) {
    BYTE descriptor[596]={1};
    descriptor[4]=0x44;descriptor[40]=0x80;descriptor[72]=sizeof(peer_file)-1;
    const char* name="remote.txt";
    for (int i=0;name[i];i++) descriptor[76+i*2]=(BYTE)name[i];
    CLIPRDR_FORMAT_DATA_RESPONSE data={.common={.msgType=CB_FORMAT_DATA_RESPONSE,.msgFlags=CB_RESPONSE_OK}};
    data.common.dataLen=req->requestedFormatId==13?sizeof(peer_text):sizeof(descriptor);
    data.requestedFormatData=req->requestedFormatId==13?peer_text:descriptor;
    return c->ServerFormatDataResponse(c,&data);
}
static UINT peer_file_request(CliprdrServerContext* c,const CLIPRDR_FILE_CONTENTS_REQUEST* req) {
    uint64_t position=((uint64_t)req->nPositionHigh<<32)|req->nPositionLow;
    if (req->dwFlags!=FILECONTENTS_RANGE || req->listIndex!=0 || position>sizeof(peer_file)-1) return ERROR_INVALID_DATA;
    UINT32 count=(UINT32)(sizeof(peer_file)-1-position);
    if (count>req->cbRequested) count=req->cbRequested;
    CLIPRDR_FILE_CONTENTS_RESPONSE data={.common={.msgType=CB_FILECONTENTS_RESPONSE,.msgFlags=CB_RESPONSE_OK},.streamId=req->streamId,.cbRequested=count,.requestedData=peer_file+position};
    return c->ServerFileContentsResponse(c,&data);
}
static BOOL peer_start(testPeerContext* context) {
    if (!WTSVirtualChannelManagerIsChannelJoined(context->vcm,"cliprdr")) {
        fprintf(stderr,"FAIL: client did not join cliprdr\n");return FALSE;
    }
    CliprdrServerContext* c=context->clipboard=cliprdr_server_context_new(context->vcm);
    if (!c) return FALSE;
    c->rdpcontext=&context->_p;c->useLongFormatNames=TRUE;c->streamFileClipEnabled=TRUE;c->fileClipNoFilePaths=TRUE;
    c->ClientFormatList=peer_list;c->ClientFormatDataResponse=peer_data;c->ClientFormatDataRequest=peer_request;
    c->ClientFileContentsResponse=peer_file_response;c->ClientFileContentsRequest=peer_file_request;
    return c->Start(c)==CHANNEL_RC_OK;
}
