"""Build a clipboard-enabled local fixture from official FreeRDP 3.26.0 source.
The source is supplied explicitly; no downloaded code runs implicitly in ordinary tests.
"""
import pathlib, shutil, subprocess, sys
source, target, prefix = map(pathlib.Path, sys.argv[1:4])
shutil.copytree(source / 'server/Sample', target)
header = target / 'sfreerdp.h'
s = header.read_text().replace('struct test_peer_context\n', '#include <freerdp/server/cliprdr.h>\nstruct test_peer_context\n')
s = s.replace('HANDLE vcm;', 'HANDLE vcm;\n    CliprdrServerContext* clipboard;')
header.write_text(s)
file = target / 'sfreerdp.c'
s = file.read_text().replace('static BOOL tf_peer_post_connect(', '#include "clipboard-peer.inc"\nstatic BOOL tf_peer_post_connect(', 1)
s = s.replace('if (WTSVirtualChannelManagerIsChannelJoined(context->vcm, "rdpdbg"))', 'if (!peer_start(context)) return FALSE;\n    if (WTSVirtualChannelManagerIsChannelJoined(context->vcm, "rdpdbg"))', 1)
s = s.replace('winpr_image_free(context->image, TRUE);', 'if (context->clipboard) { context->clipboard->Stop(context->clipboard); cliprdr_server_context_free(context->clipboard); }\n        winpr_image_free(context->image, TRUE);', 1)
file.write_text(s)
shutil.copyfile(pathlib.Path(__file__).with_name('clipboard-peer.inc'), target / 'clipboard-peer.inc')
subprocess.run(['xcrun','clang','-std=c2x','-Wno-deprecated-declarations','-Wno-unused-result',
    '-DSAMPLE_RESOURCE_ROOT="'+str(target)+'"', '-I'+str(prefix/'include/freerdp3'), '-I'+str(prefix/'include/winpr3'),
    *map(str,target.glob('*.c')), '-L'+str(prefix/'lib'), '-lfreerdp-server3','-lfreerdp3','-lwinpr3','-o',str(target/'server')],check=True)
