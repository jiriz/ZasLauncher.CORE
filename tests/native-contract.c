#include <assert.h>
#include <stdio.h>
#include <fcntl.h>
#include "../ZasLauncherGUI/Native/zas_rdp.c"
static int requested, announced;
static UINT format_request(CliprdrClientContext* c, const CLIPRDR_FORMAT_DATA_REQUEST* r) {
    (void)c; requested = r->requestedFormatId; return CHANNEL_RC_OK;
}
static UINT list_response(CliprdrClientContext* c, const CLIPRDR_FORMAT_LIST_RESPONSE* r) {
    (void)c; assert(r->common.msgFlags == CB_RESPONSE_OK); return CHANNEL_RC_OK;
}
static UINT data_response(CliprdrClientContext* c, const CLIPRDR_FORMAT_DATA_RESPONSE* r) {
    (void)c; assert(r->common.msgFlags == CB_RESPONSE_OK);
    assert(r->common.dataLen == 8); // three UTF-16 characters + terminator
    assert(r->requestedFormatData[0] == 'A' && r->requestedFormatData[6] == 0);
    return CHANNEL_RC_OK;
}
static UINT format_list(CliprdrClientContext* c, const CLIPRDR_FORMAT_LIST* r) {
    (void)c; assert(r->numFormats == 1 && r->formats[0].formatId == 13); announced++;
    return CHANNEL_RC_OK;
}
static UINT32 stream_id;
static BYTE returned_file[1024];
static UINT32 returned_count;
static UINT16 returned_flags;
static UINT file_request(CliprdrClientContext* c,const CLIPRDR_FILE_CONTENTS_REQUEST* r) {
    (void)c;stream_id=r->streamId;
    assert(r->listIndex==0 && r->dwFlags==FILECONTENTS_RANGE && r->cbRequested==4);
    return CHANNEL_RC_OK;
}
static UINT file_response(CliprdrClientContext* c,const CLIPRDR_FILE_CONTENTS_RESPONSE* r) {
    (void)c;returned_count=r->cbRequested;returned_flags=r->common.msgFlags;
    assert(returned_count<=sizeof(returned_file));
    if (returned_count) memcpy(returned_file,r->requestedData,returned_count);
    return CHANNEL_RC_OK;
}
static void test_files(Session* s,CliprdrClientContext* c) {
    c->ClientFileContentsRequest=file_request;c->ClientFileContentsResponse=file_response;
    char path[]="/tmp/zas-file-XXXXXX";int fd=mkstemp(path);assert(fd>=0);
    assert(write(fd,"abcdef",6)==6);close(fd);
    BYTE descriptor[596]={1};const char* paths[]={path};
    assert(zr_set_files(s,descriptor,sizeof(descriptor),paths,1));
    CLIPRDR_FILE_CONTENTS_REQUEST req={.streamId=5,.listIndex=0,.dwFlags=FILECONTENTS_SIZE};
    assert(clip_file_request(c,&req)==CHANNEL_RC_OK && returned_count==8 && returned_file[0]==6);
    req.dwFlags=FILECONTENTS_RANGE;req.nPositionLow=2;req.cbRequested=3;
    assert(clip_file_request(c,&req)==CHANNEL_RC_OK && returned_count==3 && !memcmp(returned_file,"cde",3));
    req.listIndex=1;assert(clip_file_request(c,&req)==CHANNEL_RC_OK && returned_flags==CB_RESPONSE_FAIL);
    fd=open(path,O_WRONLY);assert(fd>=0);
    assert(lseek(fd,((off_t)1<<32)+3,SEEK_SET)==(((off_t)1<<32)+3));assert(write(fd,"Z",1)==1);close(fd);
    req.listIndex=0;req.dwFlags=FILECONTENTS_SIZE;
    assert(clip_file_request(c,&req)==CHANNEL_RC_OK && returned_count==8 && returned_file[0]==4 && returned_file[4]==1);
    req.dwFlags=FILECONTENTS_RANGE;req.nPositionHigh=1;req.nPositionLow=3;req.cbRequested=1;
    assert(clip_file_request(c,&req)==CHANNEL_RC_OK && returned_count==1 && returned_file[0]=='Z');
    unlink(path);
    CLIPRDR_FORMAT format={.formatId=0xc002,.formatName="FileGroupDescriptorW"};
    CLIPRDR_FORMAT_LIST list={.numFormats=1,.formats=&format};
    assert(clip_list(c,&list)==CHANNEL_RC_OK && clipboard_drain(s));
    CLIPRDR_FORMAT_DATA_RESPONSE response={.common={.msgFlags=CB_RESPONSE_OK,.dataLen=sizeof(descriptor)},.requestedFormatData=descriptor};
    assert(clip_response(c,&response)==CHANNEL_RC_OK);
    int kind;uint64_t gen;BYTE copy[596];
    assert(zr_clip_info(s,&kind,&gen,copy,sizeof(copy))==596 && kind==2 && !memcmp(copy,descriptor,596));
    assert(zr_file_start(s,gen,0,0,4));assert(clipboard_drain(s));
    BYTE bytes[]={10,11,12,13};
    CLIPRDR_FILE_CONTENTS_RESPONSE data={.common.msgFlags=CB_RESPONSE_OK,.streamId=stream_id,.cbRequested=4,.requestedData=bytes};
    assert(clip_file_response(c,&data)==CHANNEL_RC_OK);
    assert(zr_file_read(s,gen,copy,sizeof(copy))==4 && !memcmp(copy,bytes,4));
    assert(zr_file_start(s,gen,0,0,4));assert(clipboard_drain(s));
    zr_file_cancel(s);assert(clip_file_response(c,&data)==CHANNEL_RC_OK);
    assert(zr_file_read(s,gen,copy,sizeof(copy))==-2);
    assert(zr_file_start(s,gen,0,0,4));assert(clipboard_drain(s));
    assert(clip_list(c,&list)==CHANNEL_RC_OK);
    assert(zr_file_read(s,gen,copy,sizeof(copy))==-2 && !zr_file_start(s,gen,0,0,4));
    // A text offer drops all locally served file paths.
    assert(zr_set_clip(s,bytes,4) && s->local_path_count==0);
    puts("PASS: file clipboard size/range serving, descriptors, incoming chunks, stale generation and cancellation");
}
int main(void) {
    Session s = {0}; pthread_mutex_init(&s.mutex, NULL);
    BYTE source[] = {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16};
    BYTE target[24]; memset(target, 0xff, sizeof(target));
    s.pixels = source; s.width = s.height = 2; s.serial = 1;
    int w,h; uint64_t serial = 0;
    assert(zr_frame(&s,target,12,2,2,&w,&h,&serial) == 1);
    assert(w == 2 && h == 2 && target[0]==1 && target[12]==9 && target[8]==0xff && target[20]==0xff);
    assert(zr_frame(&s,target,12,2,2,&w,&h,&serial) == 0); // unchanged frame
    serial = 0; assert(zr_frame(&s,target,12,1,2,&w,&h,&serial) == 0); // resize race: do not overrun
    atomic_store(&s.state,2);
    for (int i=0;i<10000;i++) assert(zr_input(&s,1,PTR_FLAGS_MOVE,i,0)); // coalesced
    assert(s.tail == 1);
    assert(zr_input(&s,2,0x1d,1,0));
    assert(zr_input(&s,2,0x1d,0,0));
    assert(s.events[1].b==1 && s.events[2].b==0);
    atomic_store(&s.stop,1); assert(!zr_input(&s,2,0x1d,1,0));
    CliprdrClientContext clip = {.custom=&s, .ClientFormatDataRequest=format_request,
        .ClientFormatListResponse=list_response, .ClientFormatDataResponse=data_response, .ClientFormatList=format_list};
    s.clipboard = &clip;
    BYTE unicode[] = {'A',0,0x0d,0,0x0a,0};
    assert(zr_set_clip(&s,unicode,sizeof(unicode)));
    assert(!zr_set_clip(&s,unicode,1)); // reject malformed UTF-16
    assert(!zr_set_clip(&s,unicode,MAX_CLIP+2));
    s.clip_ready=TRUE;
    assert(advertise_clip(&s)==CHANNEL_RC_OK && announced==1);
    assert(zr_clip_sent(&s)==0);s.clip_dirty=FALSE;
    CLIPRDR_FORMAT_LIST_RESPONSE ack={.common.msgFlags=CB_RESPONSE_OK};
    assert(clip_list_response(&clip,&ack)==CHANNEL_RC_OK && zr_clip_sent(&s)==1);
    CLIPRDR_FORMAT_DATA_REQUEST request={.requestedFormatId=13};
    assert(clip_request(&clip,&request)==CHANNEL_RC_OK);
    CLIPRDR_FORMAT format={.formatId=13}; CLIPRDR_FORMAT_LIST list={.numFormats=1,.formats=&format};
    assert(clip_list(&clip,&list)==CHANNEL_RC_OK && clipboard_drain(&s) && requested==13);
    CLIPRDR_FORMAT_DATA_RESPONSE response={.common={.msgFlags=CB_RESPONSE_OK,.dataLen=sizeof(unicode)},.requestedFormatData=unicode};
    assert(clip_response(&clip,&response)==CHANNEL_RC_OK);
    BYTE text[8]; serial=0; assert(zr_get_clip(&s,text,8,&serial)==8 && text[6]==0 && serial==1);
    assert(zr_get_clip(&s,text,8,&serial)==0);
    list.numFormats=0; assert(clip_list(&clip,&list)==CHANNEL_RC_OK); serial=0;
    assert(zr_get_clip(&s,text,8,&serial)==0); // cleared remote text, no stale data
    test_files(&s,&clip);
    clipboard_free(&s);
    free(s.local_clip); free(s.remote_clip); pthread_mutex_destroy(&s.mutex);
    puts("PASS: frame stride/resize race, bounded input/coalescing, Unicode clipboard protocol and limits");
}
