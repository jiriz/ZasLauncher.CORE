#include <assert.h>
#include <stdio.h>
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
    assert(advertise_clip(&s)==CHANNEL_RC_OK && announced==1);
    CLIPRDR_FORMAT_DATA_REQUEST request={.requestedFormatId=13};
    assert(clip_request(&clip,&request)==CHANNEL_RC_OK);
    CLIPRDR_FORMAT format={.formatId=13}; CLIPRDR_FORMAT_LIST list={.numFormats=1,.formats=&format};
    assert(clip_list(&clip,&list)==CHANNEL_RC_OK && requested==13);
    CLIPRDR_FORMAT_DATA_RESPONSE response={.common={.msgFlags=CB_RESPONSE_OK,.dataLen=sizeof(unicode)},.requestedFormatData=unicode};
    assert(clip_response(&clip,&response)==CHANNEL_RC_OK);
    BYTE text[8]; serial=0; assert(zr_get_clip(&s,text,8,&serial)==8 && text[6]==0 && serial==1);
    assert(zr_get_clip(&s,text,8,&serial)==0);
    list.numFormats=0; assert(clip_list(&clip,&list)==CHANNEL_RC_OK); serial=0;
    assert(zr_get_clip(&s,text,8,&serial)==0); // cleared remote text, no stale data
    free(s.local_clip); free(s.remote_clip); pthread_mutex_destroy(&s.mutex);
    puts("PASS: frame stride/resize race, bounded input/coalescing, Unicode clipboard protocol and limits");
}
