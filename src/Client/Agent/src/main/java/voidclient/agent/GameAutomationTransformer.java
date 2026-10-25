package voidclient.agent;

import java.lang.instrument.ClassFileTransformer;
import java.util.List;
import java.util.Set;
import java.security.ProtectionDomain;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.Type;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.LdcInsnNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.VarInsnNode;

final class GameAutomationTransformer implements ClassFileTransformer {
    private static final String HooksName = VoidClientAgent.GameHooksName;
    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined,
                            ProtectionDomain protectionDomain, byte[] classFileBuffer) {
        if (className == null || className.startsWith("java/") || className.startsWith("voidclient/agent/"))
            return null;

        boolean client = false;

        try {
            GameAutomationIndex.IndexedCode index = GameAutomationIndex.index(loader, className, protectionDomain);

            if (index == null)
                return null;

            GameAutomationController.registerIndex(loader, index);
            client = index.plan.frame.owner.equals(className);

            List<String> rejections = index.plan.rejectionCallbacks.get(className);
            boolean network = index.plan.networkTypes.contains(className);
            if (!client && rejections == null && !network)
                return null;

            ClassNode type = new ClassNode();
            new ClassReader(classFileBuffer).accept(type, 0);
            boolean changed = client && instrument(type, index.plan.frame, classBeingRedefined != null);

            // Without the client frame hook nothing the agent follows or automates ever runs.
            if (client && !changed && !hasClientHook(type))
                GameAutomationController.recordIndexFailure(GameAutomationController.ClientFrameFailure, "No client frame call was found in " + className);

            if (rejections != null)
                changed |= instrumentRejections(type, rejections, index.plan.networkTypes);

            if (network)
                changed |= instrumentConnection(type, reasonTypes(index.plan.rejectionCallbacks));

            if (!changed)
                return null;

            if (client)
                System.err.println("Void client agent instrumented client-thread driver " + className);

            ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
            type.accept(writer);
            return writer.toByteArray();
        } catch (Throwable exception) {
            String location = client ? GameAutomationController.ClientFrameFailure : className;
            GameAutomationController.recordIndexFailure(location, exception.getClass().getName() + ": " + exception.getMessage());
            return null;
        }
    }

    /**
     * Calls the agent once per client frame, from the loop right before it runs the frame. A client class that is already loaded, such as
     * when the agent attaches to a running game, is already in that loop, which never returns and so never runs the new code. There the
     * call goes at the start of the frame instead, which the loop enters again on the next frame.
     */
    static boolean instrument(ClassNode type, FramePlan frame, boolean alreadyLoaded) {
        if (alreadyLoaded && hasClientHook(type))
            return false;

        if (alreadyLoaded && instrumentFrameStart(type, frame))
            return true;

        boolean changed = false;

        for (MethodNode method : type.methods) {
            if (!frame.loopName.equals(method.name) || !frame.loopDescriptor.equals(method.desc)
                || alreadyInstrumented(method, "applyClient"))
                continue;

            for (AbstractInsnNode instruction : method.instructions.toArray()) {
                if (!(instruction instanceof MethodInsnNode))
                    continue;
                MethodInsnNode call = (MethodInsnNode) instruction;
                if (type.name.equals(call.owner) && frame.frameName.equals(call.name) && frame.frameDescriptor.equals(call.desc)) {
                    method.instructions.insertBefore(call, controllerCall("applyClient"));
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static boolean instrumentFrameStart(ClassNode type, FramePlan frame) {
        for (MethodNode method : type.methods) {
            if (!frame.frameName.equals(method.name) || !frame.frameDescriptor.equals(method.desc) || (method.access & Opcodes.ACC_STATIC) != 0)
                continue;

            method.instructions.insert(controllerCall("applyClient"));
            return true;
        }

        return false;
    }

    static boolean instrumentRejections(ClassNode type, List<String> callbacks, Set<String> networkTypes) {
        boolean changed = false;
        for (MethodNode method : type.methods) {
            if ("<init>".equals(method.name) && !alreadyInstrumented(method, "connectionListenerCreated")) {
                for (AbstractInsnNode instruction : method.instructions.toArray()) {
                    if (!(instruction instanceof FieldInsnNode) || instruction.getOpcode() != Opcodes.PUTFIELD)
                        continue;
                    FieldInsnNode field = (FieldInsnNode) instruction;
                    if (!field.desc.startsWith("L") || !networkTypes.contains(field.desc.substring(1, field.desc.length() - 1)))
                        continue;
                    // Preserve the live receiver/value on the operand stack. Optimized
                    // constructor stack maps can legally discard local zero before RETURN.
                    method.instructions.insertBefore(instruction, new InsnNode(Opcodes.DUP2));
                    method.instructions.insert(instruction, new MethodInsnNode(Opcodes.INVOKESTATIC, HooksName,
                        "connectionListenerCreated", "(Ljava/lang/Object;Ljava/lang/Object;)V", false));
                    changed = true;
                }
            }
            if (callbacks.contains(method.name + method.desc) && !alreadyInstrumented(method, "connectionRejected")) {
                InsnList call = new InsnList();
                call.add(new VarInsnNode(Opcodes.ALOAD, 0));
                call.add(new VarInsnNode(Opcodes.ALOAD, 1));
                call.add(new MethodInsnNode(Opcodes.INVOKESTATIC, HooksName, "connectionRejected", "(Ljava/lang/Object;Ljava/lang/Object;)V", false));
                method.instructions.insert(call);
                changed = true;
            }
        }
        return changed;
    }

    /**
     * Reports what a Minecraft connection goes through as it ends: each packet it reads, its channel going inactive, the exceptions it
     * catches and each reason it's disconnected with. The game takes the reason of a disconnect only while the channel is open, so these
     * tell whether the reason a server sent reached the game before the channel closed. A connection is the network handler that reads packets in {@code channelRead0},
     * which netty names, and its disconnect methods take the reason the disconnect callbacks of its listeners do.
     */
    static boolean instrumentConnection(ClassNode type, Set<String> reasonTypes) {
        boolean reads = false;

        for (MethodNode method : type.methods)
            reads |= "channelRead0".equals(method.name) && isHandlerMethod(method, 2);

        if (!reads)
            return false;

        boolean changed = false;

        for (MethodNode method : type.methods) {
            if ((method.access & (Opcodes.ACC_STATIC | Opcodes.ACC_ABSTRACT | Opcodes.ACC_BRIDGE)) != 0 || alreadyInstrumented(method, "connectionEvent"))
                continue;

            Type[] arguments = Type.getArgumentTypes(method.desc);
            String event;
            int detail;

            if ("channelRead0".equals(method.name) && isHandlerMethod(method, 2)) {
                event = ConnectionTrace.Read;
                detail = 2;
            } else if ("channelInactive".equals(method.name) && isHandlerMethod(method, 1)) {
                event = ConnectionTrace.Inactive;
                detail = 0;
            } else if ("exceptionCaught".equals(method.name) && isHandlerMethod(method, 2) && "java/lang/Throwable".equals(arguments[1].getInternalName())) {
                event = ConnectionTrace.Exception;
                detail = 2;
            } else if (arguments.length == 1 && arguments[0].getSort() == Type.OBJECT && reasonTypes.contains(arguments[0].getInternalName())
                && Type.getReturnType(method.desc).getSort() == Type.VOID) {
                event = ConnectionTrace.Disconnect;
                detail = 1;
                recordReasonBeforeClose(type, method);
            } else
                continue;

            InsnList call = new InsnList();
            call.add(new LdcInsnNode(event));
            call.add(new VarInsnNode(Opcodes.ALOAD, 0));
            call.add(detail == 0 ? new InsnNode(Opcodes.ACONST_NULL) : new VarInsnNode(Opcodes.ALOAD, detail));
            call.add(new MethodInsnNode(Opcodes.INVOKESTATIC, HooksName, "connectionEvent", "(Ljava/lang/String;Ljava/lang/Object;Ljava/lang/Object;)V", false));
            method.instructions.insert(call);
            changed = true;
        }

        return changed;
    }

    /**
     * Makes a disconnect method of a connection keep its reason before it closes the channel, rather than right after. The game handles a
     * closed channel on its own thread, which can do so in between and tell its listener a generic "Disconnected" instead of the reason a
     * server sent. The reason is kept under the same condition the method keeps it, an open channel, so a later disconnect doesn't replace it.
     *
     * @return whether the method keeps its reason after it closes the channel
     */
    static boolean recordReasonBeforeClose(ClassNode type, MethodNode method) {
        String reasonDescriptor = Type.getArgumentTypes(method.desc)[0].getDescriptor();
        MethodInsnNode close = null;

        for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
            if (instruction instanceof MethodInsnNode) {
                MethodInsnNode call = (MethodInsnNode) instruction;

                if ("close".equals(call.name) && call.desc.startsWith("()") && call.owner.endsWith("io/netty/channel/Channel"))
                    close = call;

                continue;
            }

            if (close == null || instruction.getOpcode() != Opcodes.PUTFIELD || !(instruction.getPrevious() instanceof VarInsnNode))
                continue;

            FieldInsnNode field = (FieldInsnNode) instruction;
            VarInsnNode value = (VarInsnNode) instruction.getPrevious();

            if (!type.name.equals(field.owner) || !reasonDescriptor.equals(field.desc) || value.getOpcode() != Opcodes.ALOAD || value.var != 1)
                continue;

            // Leaves the stack as it was, so the channel the close call takes stays under it
            InsnList keep = new InsnList();
            keep.add(new VarInsnNode(Opcodes.ALOAD, 0));
            keep.add(new VarInsnNode(Opcodes.ALOAD, 1));
            keep.add(new FieldInsnNode(Opcodes.PUTFIELD, field.owner, field.name, field.desc));
            method.instructions.insertBefore(close, keep);
            return true;
        }

        return false;
    }

    /** Reads the types of the reasons the disconnect callbacks of network listeners take. */
    static Set<String> reasonTypes(java.util.Map<String, List<String>> rejectionCallbacks) {
        Set<String> types = new java.util.HashSet<String>();

        for (List<String> callbacks : rejectionCallbacks.values())
            for (String callback : callbacks)
                types.add(Type.getArgumentTypes(callback.substring(callback.indexOf('(')))[0].getInternalName());

        return types;
    }

    // Netty's handler methods take the context first, which mod loaders may not relocate but some builds shade.
    private static boolean isHandlerMethod(MethodNode method, int argumentCount) {
        Type[] arguments = Type.getArgumentTypes(method.desc);

        return arguments.length == argumentCount && Type.getReturnType(method.desc).getSort() == Type.VOID
            && (method.access & (Opcodes.ACC_STATIC | Opcodes.ACC_BRIDGE)) == 0
            && arguments[0].getSort() == Type.OBJECT && arguments[0].getInternalName().endsWith("io/netty/channel/ChannelHandlerContext")
            && (argumentCount < 2 || arguments[1].getSort() == Type.OBJECT);
    }

    private static InsnList controllerCall(String controllerMethod) {
        InsnList call = new InsnList();
        call.add(new VarInsnNode(Opcodes.ALOAD, 0));
        call.add(new MethodInsnNode(Opcodes.INVOKESTATIC, HooksName, controllerMethod, "(Ljava/lang/Object;)V", false));
        return call;
    }

    private static boolean hasClientHook(ClassNode type) {
        for (MethodNode method : type.methods)
            if (alreadyInstrumented(method, "applyClient"))
                return true;

        return false;
    }

    private static boolean alreadyInstrumented(MethodNode method, String controllerMethod) {
        for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
            if (instruction instanceof MethodInsnNode) {
                MethodInsnNode call = (MethodInsnNode) instruction;

                if (HooksName.equals(call.owner) && controllerMethod.equals(call.name))
                    return true;
            }
        }

        return false;
    }
}
