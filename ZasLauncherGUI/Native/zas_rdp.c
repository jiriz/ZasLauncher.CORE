// FreeRDP 3 adapter. Only the worker thread calls protocol/input APIs.
// UI calls copy/queue operations; no managed callbacks survive session disposal.
#include <freerdp/freerdp.h>
#include <freerdp/gdi/gdi.h>
#include <freerdp/client/cmdline.h>
#include <freerdp/client/channels.h>
#include <freerdp/client/disp.h>
#include <freerdp/client/cliprdr.h>
#include <freerdp/input.h>
#include <freerdp/graphics.h>
#include <freerdp/codec/color.h>
#include <winpr/synch.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <sys/stat.h>

#define API __attribute__((visibility("default")))
#define QUEUE_SIZE 2048
#define MAX_CLIP (1024 * 1024)
typedef struct { int type, a, b, c; } Event;
typedef struct Session Session;
typedef struct { rdpContext base; Session* owner; } Context;
struct Session {
    freerdp* instance;
    pthread_mutex_t mutex;
    atomic_int stop, state;
    BYTE* pixels;
    int width, height;
    uint64_t serial;
    BYTE cursor[256*256*4];
    int cursor_width, cursor_height, cursor_x, cursor_y;
    uint64_t cursor_serial;
    int requested_width, requested_height;
    Event events[QUEUE_SIZE];
    unsigned head, tail;
    DispClientContext* display;
    atomic_bool display_ready;
    CliprdrClientContext* clipboard;
    BYTE *local_clip, *remote_clip;
    int local_size, remote_size;
    uint64_t clip_serial, local_generation, advertised_generation, acknowledged_generation;
    int clipboard_ack_error;
    BOOL clip_dirty, clip_ready;
    BYTE *local_descriptors, *remote_descriptors;
    int local_descriptor_size, remote_descriptor_size;
    char** local_paths;
    int local_path_count;
    int remote_kind, request_kind;
    UINT32 remote_format;
    uint64_t remote_generation, data_generation, request_generation;
    UINT32 next_stream, file_stream;
    uint64_t file_generation, file_offset;
    UINT32 file_index, file_wanted;
    int file_status, file_size; // -1 pending, -2 failed, 0 idle, 1 queued, 2 ready
    BYTE* file_buffer;
    UINT32 server_clip_flags;
};
static pthread_once_t once = PTHREAD_ONCE_INIT;
static void init_addins(void) {
    freerdp_register_addin_provider(freerdp_channels_load_static_addin_entry, 0);
}
static Session* session(rdpContext* c) { return ((Context*)c)->owner; }
static BOOL begin_paint(rdpContext* c) {
    c->gdi->primary->hdc->hwnd->invalid->null = TRUE;
    return TRUE;
}
static BOOL end_paint(rdpContext* c) {
    Session* s = session(c);
    rdpGdi* g = c->gdi;
    if (g->primary->hdc->hwnd->invalid->null) return TRUE;
    if (g->width < 1 || g->height < 1 || g->width > 8192 || g->height > 8192) return FALSE;
    pthread_mutex_lock(&s->mutex);
    if (s->width != g->width || s->height != g->height) {
        BYTE* p = realloc(s->pixels, (size_t)g->width * g->height * 4);
        if (!p) { pthread_mutex_unlock(&s->mutex); return FALSE; }
        s->pixels = p; s->width = g->width; s->height = g->height;
    }
    for (int y = 0; y < s->height; ++y)
        memcpy(s->pixels + (size_t)y * s->width * 4,
               g->primary_buffer + (size_t)y * g->stride, (size_t)s->width * 4);
    ++s->serial;
    pthread_mutex_unlock(&s->mutex);
    return TRUE;
}
static BOOL desktop_resize(rdpContext* c) {
    return gdi_resize(c->gdi, freerdp_settings_get_uint32(c->settings, FreeRDP_DesktopWidth),
                     freerdp_settings_get_uint32(c->settings, FreeRDP_DesktopHeight));
}
static UINT display_caps(DispClientContext* d, UINT32 n, UINT32 a, UINT32 b) {
    (void)n; (void)a; (void)b;
    ((Session*)d->custom)->display_ready = TRUE;
    return CHANNEL_RC_OK;
}
#include "zas_clipboard.inc"
static void channel_connected(void* context, const ChannelConnectedEventArgs* e) {
    Session* s = session(context);
    if (strcmp(e->name, DISP_DVC_CHANNEL_NAME) == 0) {
        s->display = e->pInterface; s->display->custom = s;
        s->display->DisplayControlCaps = display_caps;
    } else if (strcmp(e->name, "cliprdr") == 0) {
        s->clipboard = e->pInterface; s->clipboard->custom = s;
        s->clipboard->ServerCapabilities = clip_caps;
        s->clipboard->MonitorReady = clip_ready;
        s->clipboard->ServerFormatList = clip_list;
        s->clipboard->ServerFormatListResponse = clip_list_response;
        s->clipboard->ServerFormatDataRequest = clip_request;
        s->clipboard->ServerFormatDataResponse = clip_response;
        s->clipboard->ServerFileContentsRequest = clip_file_request;
        s->clipboard->ServerFileContentsResponse = clip_file_response;
    }
}
static void channel_disconnected(void* context, const ChannelDisconnectedEventArgs* e) {
    Session* s = session(context);
    if (strcmp(e->name, DISP_DVC_CHANNEL_NAME) == 0) { s->display = NULL; s->display_ready = FALSE; }
    if (strcmp(e->name, "cliprdr") == 0) {
        s->clipboard = NULL;
        pthread_mutex_lock(&s->mutex); s->clip_ready = FALSE; pthread_mutex_unlock(&s->mutex);
    }
}
static BOOL pre_connect(freerdp* i) {
    if (PubSub_SubscribeChannelConnected(i->context->pubSub, channel_connected) < 0) return FALSE;
    if (PubSub_SubscribeChannelDisconnected(i->context->pubSub, channel_disconnected) < 0) return FALSE;
    return freerdp_client_load_addins(i->context->channels, i->context->settings);
}
static BOOL pointer_new(rdpContext* c, rdpPointer* p) { (void)c; (void)p; return TRUE; }
static void pointer_free(rdpContext* c, rdpPointer* p) { (void)c; (void)p; }
static BOOL pointer_set(rdpContext* c, const rdpPointer* p) {
    Session* s = session(c);
    if (p->width > 256 || p->height > 256 || !p->width || !p->height) return TRUE;
    pthread_mutex_lock(&s->mutex);
    BOOL ok = freerdp_image_copy_from_pointer_data(s->cursor, PIXEL_FORMAT_BGRA32, p->width*4,
        0, 0, p->width, p->height, p->xorMaskData, p->lengthXorMask,
        p->andMaskData, p->lengthAndMask, p->xorBpp, NULL);
    if (ok) {
        s->cursor_width = p->width; s->cursor_height = p->height;
        s->cursor_x = p->xPos; s->cursor_y = p->yPos; ++s->cursor_serial;
    }
    pthread_mutex_unlock(&s->mutex); return ok;
}
static BOOL pointer_set_callback(rdpContext* c, rdpPointer* p) { return pointer_set(c, p); }
static BOOL pointer_default(rdpContext* c) {
    Session* s = session(c); pthread_mutex_lock(&s->mutex);
    s->cursor_width = 0; ++s->cursor_serial; pthread_mutex_unlock(&s->mutex); return TRUE;
}
static BOOL pointer_null(rdpContext* c) {
    Session* s = session(c); pthread_mutex_lock(&s->mutex);
    s->cursor_width = -1; ++s->cursor_serial; pthread_mutex_unlock(&s->mutex); return TRUE;
}
static BOOL post_connect(freerdp* i) {
    if (!gdi_init(i, PIXEL_FORMAT_BGRA32)) return FALSE;
    i->context->update->BeginPaint = begin_paint;
    i->context->update->EndPaint = end_paint;
    i->context->update->DesktopResize = desktop_resize;
    rdpPointer pointer = { .size = sizeof(rdpPointer), .New = pointer_new, .Free = pointer_free,
        .Set = pointer_set_callback, .SetNull = pointer_null, .SetDefault = pointer_default };
    graphics_register_pointer(i->context->graphics, &pointer);
    atomic_store(&session(i->context)->state, 2);
    return TRUE;
}
static void post_disconnect(freerdp* i) {
    PubSub_UnsubscribeChannelConnected(i->context->pubSub, channel_connected);
    PubSub_UnsubscribeChannelDisconnected(i->context->pubSub, channel_disconnected);
    if (i->context->gdi) gdi_free(i);
}
API Session* zr_create(const char* host, int port, const char* user, const char* password,
                      int width, int height, int keyboard_layout) {
    pthread_once(&once, init_addins);
    Session* s = calloc(1, sizeof(Session));
    if (!s) return NULL;
    pthread_mutex_init(&s->mutex, NULL);
    s->instance = freerdp_new();
    if (!s->instance) goto fail;
    s->instance->ContextSize = sizeof(Context);
    s->instance->PreConnect = pre_connect;
    s->instance->PostConnect = post_connect;
    s->instance->PostDisconnect = post_disconnect;
    if (!freerdp_context_new(s->instance)) goto fail;
    ((Context*)s->instance->context)->owner = s;
    rdpSettings* p = s->instance->context->settings;
    const char* slash = strchr(user, '\\');
    char* domain = slash ? strndup(user, (size_t)(slash-user)) : NULL;
    BOOL ok = freerdp_settings_set_string(p, FreeRDP_ServerHostname, host) &&
        freerdp_settings_set_uint32(p, FreeRDP_ServerPort, port) &&
        freerdp_settings_set_string(p, FreeRDP_Username, slash ? slash + 1 : user) &&
        freerdp_settings_set_string(p, FreeRDP_Password, password) &&
        freerdp_settings_set_string(p, FreeRDP_Domain, domain ? domain : "") &&
        freerdp_settings_set_uint32(p, FreeRDP_DesktopWidth, width) &&
        freerdp_settings_set_uint32(p, FreeRDP_DesktopHeight, height) &&
        freerdp_settings_set_uint32(p, FreeRDP_ColorDepth, 32) &&
        freerdp_settings_set_uint32(p, FreeRDP_KeyboardLayout, keyboard_layout) &&
        freerdp_settings_set_uint32(p, FreeRDP_TcpConnectTimeout, 10000) &&
        freerdp_settings_set_bool(p, FreeRDP_IgnoreCertificate, TRUE) && // preserves existing /cert:ignore
        freerdp_settings_set_bool(p, FreeRDP_NSCodec, TRUE) &&
        freerdp_settings_set_bool(p, FreeRDP_SupportGraphicsPipeline, FALSE) &&
        freerdp_settings_set_bool(p, FreeRDP_RedirectClipboard, TRUE) &&
        freerdp_settings_set_bool(p, FreeRDP_SupportDisplayControl, TRUE) &&
        freerdp_settings_set_bool(p, FreeRDP_DynamicResolutionUpdate, TRUE);
    free(domain);
    if (!ok) goto fail;
    return s;
fail:
    if (s->instance) { freerdp_context_free(s->instance); freerdp_free(s->instance); }
    pthread_mutex_destroy(&s->mutex); free(s); return NULL;
}
// Bounded queue; callers retry key/button releases on overflow. Move events coalesce.
API int zr_input(Session* s, int type, int a, int b, int c) {
    if (atomic_load(&s->state) != 2 || atomic_load(&s->stop)) return 0;
    pthread_mutex_lock(&s->mutex);
    unsigned next = (s->tail + 1) % QUEUE_SIZE;
    if (type == 1 && a == PTR_FLAGS_MOVE && s->head != s->tail) {
        unsigned last = (s->tail + QUEUE_SIZE - 1) % QUEUE_SIZE;
        if (s->events[last].type == 1 && s->events[last].a == PTR_FLAGS_MOVE) {
            s->events[last] = (Event){type,a,b,c}; pthread_mutex_unlock(&s->mutex); return 1;
        }
    }
    if (next == s->head) { pthread_mutex_unlock(&s->mutex); return 0; }
    s->events[s->tail] = (Event){type,a,b,c}; s->tail = next;
    pthread_mutex_unlock(&s->mutex); return 1;
}
static BOOL drain(Session* s) {
    for (;;) {
        pthread_mutex_lock(&s->mutex);
        if (s->head == s->tail) { pthread_mutex_unlock(&s->mutex); break; }
        Event e = s->events[s->head]; s->head = (s->head + 1) % QUEUE_SIZE;
        pthread_mutex_unlock(&s->mutex);
        rdpInput* in = s->instance->context->input;
        BOOL ok = TRUE;
        if (e.type == 1) ok = freerdp_input_send_mouse_event(in, (UINT16)e.a, (UINT16)e.b, (UINT16)e.c);
        if (e.type == 2) ok = freerdp_input_send_keyboard_event_ex(in, e.b, FALSE, (UINT32)e.a);
        if (e.type == 3) ok = freerdp_input_send_unicode_keyboard_event(in, e.b ? 0 : KBD_FLAGS_RELEASE, (UINT16)e.a);
        if (e.type == 4) {
            s->requested_width = e.a; s->requested_height = e.b;
        }
        if (!ok) return FALSE;
    }
    if (s->display && s->display_ready && s->requested_width) {
        DISPLAY_CONTROL_MONITOR_LAYOUT m = { .Flags = DISPLAY_CONTROL_MONITOR_PRIMARY,
            .Width = (UINT32)s->requested_width, .Height = (UINT32)s->requested_height,
            .PhysicalWidth = 340, .PhysicalHeight = 210,
            .DesktopScaleFactor = 100, .DeviceScaleFactor = 100 };
        if (s->display->SendMonitorLayout(s->display, 1, &m) != CHANNEL_RC_OK) return FALSE;
        s->requested_width = 0;
    }
    pthread_mutex_lock(&s->mutex);
    BOOL notify = s->clip_dirty && s->clip_ready && s->advertised_generation == s->acknowledged_generation;
    if (notify) s->clip_dirty = FALSE;
    pthread_mutex_unlock(&s->mutex);
    if (notify && s->clipboard && advertise_clip(s) != CHANNEL_RC_OK) return FALSE;
    return !s->clipboard || clipboard_drain(s);
}
API int zr_run(Session* s) {
    if (atomic_load(&s->stop)) { atomic_store(&s->state, 3); return 0; }
    atomic_store(&s->state, 1);
    BOOL connected = freerdp_connect(s->instance);
    if (connected) {
        while (!atomic_load(&s->stop) && !freerdp_shall_disconnect_context(s->instance->context)) {
            HANDLE handles[MAXIMUM_WAIT_OBJECTS];
            DWORD count = freerdp_get_event_handles(s->instance->context, handles, MAXIMUM_WAIT_OBJECTS);
            if (!count || WaitForMultipleObjects(count, handles, FALSE, 10) == WAIT_FAILED ||
                !freerdp_check_event_handles(s->instance->context) || !drain(s)) break;
        }
    }
    UINT32 error = freerdp_get_last_error(s->instance->context);
    freerdp_disconnect(s->instance);
    atomic_store(&s->state, (!connected || error) && !atomic_load(&s->stop) ? 4 : 3);
    return atomic_load(&s->stop) ? 0 : (int)error;
}
API void zr_stop(Session* s) {
    atomic_store(&s->stop, 1);
    BOOL ignored = freerdp_abort_connect_context(s->instance->context); (void)ignored;
}
API int zr_state(Session* s) { return atomic_load(&s->state); }
// Caller must await zr_run before freeing. No freeing on a timeout or during callbacks.
API void zr_free(Session* s) {
    if (!s) return;
    freerdp_context_free(s->instance); freerdp_free(s->instance);
    free(s->pixels);
    if (s->local_clip) { memset(s->local_clip, 0, s->local_size); free(s->local_clip); }
    if (s->remote_clip) { memset(s->remote_clip, 0, s->remote_size); free(s->remote_clip); }
    clipboard_free(s);
    pthread_mutex_destroy(&s->mutex); free(s);
}
API int zr_frame(Session* s, void* target, int stride, int capacityWidth, int capacityHeight,
                 int* width, int* height, uint64_t* serial) {
    pthread_mutex_lock(&s->mutex);
    *width = s->width; *height = s->height;
    int copied = 0;
    if (target && s->pixels && capacityWidth == s->width && capacityHeight == s->height &&
        stride >= s->width*4 && *serial != s->serial) {
        for (int y = 0; y < s->height; ++y)
            memcpy((BYTE*)target + (size_t)y * stride, s->pixels + (size_t)y * s->width * 4, (size_t)s->width*4);
        *serial = s->serial; copied = 1;
    }
    pthread_mutex_unlock(&s->mutex); return copied;
}
API int zr_cursor(Session* s, BYTE* target, int capacity, int* width, int* height,
                  int* x, int* y, uint64_t* serial) {
    pthread_mutex_lock(&s->mutex);
    int changed = *serial != s->cursor_serial;
    *width = s->cursor_width; *height = s->cursor_height; *x = s->cursor_x; *y = s->cursor_y;
    if (changed) {
        int bytes = s->cursor_width > 0 ? s->cursor_width * s->cursor_height * 4 : 0;
        if (capacity >= bytes) {
            if (bytes) memcpy(target, s->cursor, bytes);
            *serial = s->cursor_serial;
        } else changed = 0;
    }
    pthread_mutex_unlock(&s->mutex); return changed;
}
