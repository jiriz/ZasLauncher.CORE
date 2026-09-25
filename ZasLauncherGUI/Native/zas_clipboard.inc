// Clipboard protocol state is guarded by Session.mutex. Network sends use the worker.
#define MAX_FILES 4096
#define MAX_DESCRIPTORS (4 + MAX_FILES * 592)
#define LOCAL_FILE_FORMAT 0xC001
static void clear_local_files(Session* s) {
    for (int i=0; i<s->local_path_count; ++i) free(s->local_paths[i]);
    free(s->local_paths); s->local_paths=NULL; s->local_path_count=0;
    free(s->local_descriptors); s->local_descriptors=NULL; s->local_descriptor_size=0;
}
static void clipboard_free(Session* s) {
    clear_local_files(s); free(s->remote_descriptors); free(s->file_buffer);
}
static UINT clip_caps(CliprdrClientContext* c, const CLIPRDR_CAPABILITIES* caps) {
    Session* s=c->custom;
    pthread_mutex_lock(&s->mutex);
    const BYTE* data=(const BYTE*)caps->capabilitySets;
    for (UINT32 i=0;i<caps->cCapabilitiesSets;i++) {
        const CLIPRDR_CAPABILITY_SET* cap=(const CLIPRDR_CAPABILITY_SET*)data;
        if (cap->capabilitySetLength < sizeof(*cap)) break;
        if (cap->capabilitySetType==CB_CAPSTYPE_GENERAL && cap->capabilitySetLength>=12)
            s->server_clip_flags=((const CLIPRDR_GENERAL_CAPABILITY_SET*)cap)->generalFlags;
        data += cap->capabilitySetLength;
    }
    pthread_mutex_unlock(&s->mutex); return CHANNEL_RC_OK;
}
static UINT advertise_clip(Session* s) {
    pthread_mutex_lock(&s->mutex);
    BOOL files=s->local_descriptor_size>0;
    s->advertised_generation=s->local_generation; s->clipboard_ack_error=0;
    pthread_mutex_unlock(&s->mutex);
    CLIPRDR_FORMAT format={ .formatId=files?LOCAL_FILE_FORMAT:13,
        .formatName=files?"FileGroupDescriptorW":NULL };
    CLIPRDR_FORMAT_LIST list={ .numFormats=1, .formats=&format };
    return s->clipboard->ClientFormatList(s->clipboard,&list);
}
static UINT clip_ready(CliprdrClientContext* c,const CLIPRDR_MONITOR_READY* ready) {
    (void)ready;
    CLIPRDR_GENERAL_CAPABILITY_SET general={.capabilitySetType=CB_CAPSTYPE_GENERAL,
        .capabilitySetLength=12,.version=CB_CAPS_VERSION_2,
        .generalFlags=CB_USE_LONG_FORMAT_NAMES|CB_STREAM_FILECLIP_ENABLED|CB_FILECLIP_NO_FILE_PATHS|CB_HUGE_FILE_SUPPORT_ENABLED};
    CLIPRDR_CAPABILITIES caps={.cCapabilitiesSets=1,.capabilitySets=(CLIPRDR_CAPABILITY_SET*)&general};
    UINT rc=c->ClientCapabilities(c,&caps);
    Session* s=c->custom;
    pthread_mutex_lock(&s->mutex);s->clip_ready=TRUE;pthread_mutex_unlock(&s->mutex);
    return rc;
}
static UINT clip_list(CliprdrClientContext* c,const CLIPRDR_FORMAT_LIST* list) {
    Session* s=c->custom;
    int kind=0; UINT32 format=0;
    for (UINT32 i=0;i<list->numFormats;i++) {
        if (list->formats[i].formatId==13 && !kind) {kind=1;format=13;}
        if (list->formats[i].formatName && !strcmp(list->formats[i].formatName,"FileGroupDescriptorW")) {
            kind=2;format=list->formats[i].formatId;
        }
    }
    pthread_mutex_lock(&s->mutex);
    s->remote_kind=kind;s->remote_format=format;++s->remote_generation;
    free(s->remote_clip);s->remote_clip=NULL;s->remote_size=0;
    free(s->remote_descriptors);s->remote_descriptors=NULL;s->remote_descriptor_size=0;
    s->file_status=-2;
    pthread_mutex_unlock(&s->mutex);
    CLIPRDR_FORMAT_LIST_RESPONSE response={.common.msgFlags=CB_RESPONSE_OK};
    return c->ClientFormatListResponse(c,&response);
}
static UINT clip_list_response(CliprdrClientContext* c,const CLIPRDR_FORMAT_LIST_RESPONSE* r) {
    Session* s=c->custom;
    pthread_mutex_lock(&s->mutex);
    s->acknowledged_generation=s->advertised_generation;
    s->clipboard_ack_error=!(r->common.msgFlags&CB_RESPONSE_OK);
    pthread_mutex_unlock(&s->mutex);return CHANNEL_RC_OK;
}
static UINT clip_request(CliprdrClientContext* c,const CLIPRDR_FORMAT_DATA_REQUEST* request) {
    Session* s=c->custom;
    pthread_mutex_lock(&s->mutex);
    CLIPRDR_FORMAT_DATA_RESPONSE r={.common.msgFlags=CB_RESPONSE_FAIL};
    if (request->requestedFormatId==13 && s->local_clip) {
        r.common.msgFlags=CB_RESPONSE_OK;r.common.dataLen=s->local_size;r.requestedFormatData=s->local_clip;
    } else if (request->requestedFormatId==LOCAL_FILE_FORMAT && s->local_descriptors) {
        r.common.msgFlags=CB_RESPONSE_OK;r.common.dataLen=s->local_descriptor_size;r.requestedFormatData=s->local_descriptors;
    }
    UINT rc=c->ClientFormatDataResponse(c,&r);
    pthread_mutex_unlock(&s->mutex);return rc;
}
static UINT clip_response(CliprdrClientContext* c,const CLIPRDR_FORMAT_DATA_RESPONSE* r) {
    Session* s=c->custom;
    pthread_mutex_lock(&s->mutex);
    int kind=s->request_kind; s->request_kind=0;
    if (!kind || s->request_generation!=s->remote_generation) {pthread_mutex_unlock(&s->mutex);return CHANNEL_RC_OK;}
    s->data_generation=s->remote_generation;
    if (!(r->common.msgFlags&CB_RESPONSE_OK) || r->common.dataLen>(kind==2?MAX_DESCRIPTORS:MAX_CLIP) ||
        (!r->requestedFormatData && r->common.dataLen) || (kind==1 && (r->common.dataLen&1))) {
        s->remote_kind=-1;pthread_mutex_unlock(&s->mutex);return CHANNEL_RC_OK;
    }
    BYTE* data=calloc(1,(size_t)r->common.dataLen+2);
    if (!data) {pthread_mutex_unlock(&s->mutex);return CHANNEL_RC_NO_MEMORY;}
    if (r->common.dataLen) memcpy(data,r->requestedFormatData,r->common.dataLen);
    if (kind==2) {
        free(s->remote_descriptors);s->remote_descriptors=data;s->remote_descriptor_size=(int)r->common.dataLen;
    } else {
        free(s->remote_clip);s->remote_clip=data;s->remote_size=(int)r->common.dataLen+2;++s->clip_serial;
    }
    pthread_mutex_unlock(&s->mutex);return CHANNEL_RC_OK;
}
static UINT clip_file_request(CliprdrClientContext* c,const CLIPRDR_FILE_CONTENTS_REQUEST* request) {
    Session* s=c->custom;
    CLIPRDR_FILE_CONTENTS_RESPONSE response={.common.msgFlags=CB_RESPONSE_FAIL,.streamId=request->streamId};
    FILE* file=NULL;
    pthread_mutex_lock(&s->mutex);
    if (request->listIndex<(UINT32)s->local_path_count && !request->haveClipDataId)
        file=fopen(s->local_paths[request->listIndex],"rb");
    pthread_mutex_unlock(&s->mutex);
    BYTE* buffer=NULL;
    if (file) {
        struct stat info;
        if (!fstat(fileno(file),&info) && S_ISREG(info.st_mode)) {
            if (request->dwFlags==FILECONTENTS_SIZE) {
                buffer=malloc(8);
                if (buffer) {
                    uint64_t size=(uint64_t)info.st_size;
                    for (int i=0;i<8;i++) buffer[i]=(BYTE)(size>>(i*8));
                    response.cbRequested=8;response.common.msgFlags=CB_RESPONSE_OK;
                }
            } else if (request->dwFlags==FILECONTENTS_RANGE && request->cbRequested<=16*MAX_CLIP) {
                uint64_t offset=((uint64_t)request->nPositionHigh<<32)|request->nPositionLow;
                buffer=malloc(request->cbRequested?request->cbRequested:1);
                if (buffer && offset<=INT64_MAX && !fseeko(file,(off_t)offset,SEEK_SET)) {
                    response.cbRequested=(UINT32)fread(buffer,1,request->cbRequested,file);
                    if (!ferror(file)) response.common.msgFlags=CB_RESPONSE_OK;
                }
            }
        }
        fclose(file);
    }
    response.requestedData=buffer;
    UINT rc=c->ClientFileContentsResponse(c,&response);
    free(buffer);return rc;
}
static UINT clip_file_response(CliprdrClientContext* c,const CLIPRDR_FILE_CONTENTS_RESPONSE* response) {
    Session* s=c->custom;
    pthread_mutex_lock(&s->mutex);
    if (s->file_status==-1 && response->streamId==s->file_stream) {
        if (!(response->common.msgFlags&CB_RESPONSE_OK) || response->cbRequested>s->file_wanted ||
            s->file_generation!=s->remote_generation || (!response->requestedData && response->cbRequested)) s->file_status=-2;
        else {
            if (response->cbRequested) memcpy(s->file_buffer,response->requestedData,response->cbRequested);
            s->file_size=(int)response->cbRequested;s->file_status=2;
        }
    }
    pthread_mutex_unlock(&s->mutex);return CHANNEL_RC_OK;
}
static BOOL clipboard_drain(Session* s) {
    CLIPRDR_FORMAT_DATA_REQUEST format={0};
    CLIPRDR_FILE_CONTENTS_REQUEST file={0};
    pthread_mutex_lock(&s->mutex);
    if (s->remote_kind>0 && !s->request_kind && s->data_generation!=s->remote_generation) {
        s->request_kind=s->remote_kind;s->request_generation=s->remote_generation;
        format.requestedFormatId=s->remote_format;
    }
    if (s->file_status==1) {
        s->file_status=-1;
        file.streamId=s->file_stream;file.listIndex=s->file_index;file.dwFlags=FILECONTENTS_RANGE;
        file.nPositionLow=(UINT32)s->file_offset;file.nPositionHigh=(UINT32)(s->file_offset>>32);
        file.cbRequested=s->file_wanted;
    }
    pthread_mutex_unlock(&s->mutex);
    if (format.requestedFormatId && s->clipboard->ClientFormatDataRequest(s->clipboard,&format)!=CHANNEL_RC_OK) return FALSE;
    if (file.cbRequested && s->clipboard->ClientFileContentsRequest(s->clipboard,&file)!=CHANNEL_RC_OK) return FALSE;
    return TRUE;
}
API int zr_set_clip(Session* s,const BYTE* text,int bytes) {
    if (bytes<0 || bytes>MAX_CLIP || (bytes&1)) return 0;
    BYTE* copy=calloc(1,(size_t)bytes+2);if (!copy) return 0;
    if (bytes) memcpy(copy,text,bytes);
    pthread_mutex_lock(&s->mutex);
    clear_local_files(s);free(s->local_clip);s->local_clip=copy;s->local_size=bytes+2;s->clip_dirty=TRUE;++s->local_generation;
    pthread_mutex_unlock(&s->mutex);return 1;
}
API int zr_get_clip(Session* s,BYTE* target,int capacity,uint64_t* serial) {
    pthread_mutex_lock(&s->mutex);
    int count=*serial!=s->clip_serial?s->remote_size:0;
    if (target && count<=capacity && count>0) {memcpy(target,s->remote_clip,count);*serial=s->clip_serial;}
    pthread_mutex_unlock(&s->mutex);return count;
}
API int zr_set_files(Session* s,const BYTE* descriptors,int size,const char* const* paths,int count) {
    if (count<1 || count>MAX_FILES || size!=4+count*592) return 0;
    BYTE* data=malloc((size_t)size);char** copy=calloc((size_t)count,sizeof(char*));
    if (!data || !copy) {free(data);free(copy);return 0;}
    memcpy(data,descriptors,(size_t)size);
    for (int i=0;i<count;i++) if (!(copy[i]=strdup(paths[i]))) {
        for (int j=0;j<i;j++) free(copy[j]);free(copy);free(data);return 0;
    }
    pthread_mutex_lock(&s->mutex);
    clear_local_files(s);free(s->local_clip);s->local_clip=NULL;s->local_size=0;
    s->local_descriptors=data;s->local_descriptor_size=size;s->local_paths=copy;s->local_path_count=count;s->clip_dirty=TRUE;++s->local_generation;
    pthread_mutex_unlock(&s->mutex);return 1;
}
// Returns kind and metadata only when that clipboard generation is fully received.
API int zr_clip_info(Session* s,int* kind,uint64_t* generation,BYTE* target,int capacity) {
    pthread_mutex_lock(&s->mutex);
    *generation=s->remote_generation;
    *kind=s->data_generation==s->remote_generation?s->remote_kind:0;
    int bytes=*kind==2?s->remote_descriptor_size:(*kind==1?s->remote_size:0);
    if (target && bytes<=capacity && bytes>0)
        memcpy(target,*kind==2?s->remote_descriptors:s->remote_clip,bytes);
    pthread_mutex_unlock(&s->mutex);return bytes;
}
API int zr_file_start(Session* s,uint64_t generation,UINT32 index,uint64_t offset,UINT32 size) {
    if (!size || size>MAX_CLIP) return 0;
    pthread_mutex_lock(&s->mutex);
    if (generation!=s->remote_generation || s->file_status==-1 || s->file_status==1 || s->remote_kind!=2) {
        pthread_mutex_unlock(&s->mutex);return 0;
    }
    if (!s->file_buffer) s->file_buffer=malloc(MAX_CLIP);
    if (!s->file_buffer) {pthread_mutex_unlock(&s->mutex);return 0;}
    s->file_generation=generation;s->file_index=index;s->file_offset=offset;s->file_wanted=size;
    s->file_stream=++s->next_stream;s->file_status=1;s->file_size=0;
    pthread_mutex_unlock(&s->mutex);return 1;
}
API int zr_file_read(Session* s,uint64_t generation,BYTE* target,int capacity) {
    pthread_mutex_lock(&s->mutex);
    int result=-1;
    if (generation!=s->remote_generation || s->file_status==-2) result=-2;
    else if (s->file_status==2 && capacity>=s->file_size) {
        result=s->file_size;if (result) memcpy(target,s->file_buffer,(size_t)result);s->file_status=0;
    }
    pthread_mutex_unlock(&s->mutex);return result;
}

API void zr_file_cancel(Session* s) {
    pthread_mutex_lock(&s->mutex);s->file_status=-2;++s->next_stream;pthread_mutex_unlock(&s->mutex);
}

API int zr_clip_sent(Session* s) {
    pthread_mutex_lock(&s->mutex);
    int result=s->clipboard_ack_error?-1:(s->clip_ready && s->local_generation==s->acknowledged_generation && !s->clip_dirty);
    pthread_mutex_unlock(&s->mutex);return result;
}
