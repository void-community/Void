package voidclient.agent;

import java.lang.instrument.ClassFileTransformer;
import java.net.URL;
import java.util.List;
import java.util.Set;
import java.security.CodeSource;
import java.security.ProtectionDomain;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.InsnNode;
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
            CodeSource codeSource = protectionDomain == null ? null : protectionDomain.getCodeSource();
            URL location = codeSource == null ? null : codeSource.getLocation();
            GameAutomationIndex.IndexedCode index = GameAutomationIndex.index(location);

            if (index == null)
                return null;

            GameAutomationController.registerIndex(loader, index);
            client = index.plan.frame.owner.equals(className);

            List<String> rejections = index.plan.rejectionCallbacks.get(className);
            if (!client && rejections == null)
                return null;

            ClassNode type = new ClassNode();
            new ClassReader(classFileBuffer).accept(type, 0);
            boolean changed = client && instrument(type, index.plan.frame, classBeingRedefined != null);

            // Without the client frame hook nothing the agent follows or automates ever runs.
            if (client && !changed && !hasClientHook(type))
                GameAutomationController.recordIndexFailure(GameAutomationController.ClientFrameFailure, "No client frame call was found in " + className);

            if (rejections != null)
                changed |= instrumentRejections(type, rejections, index.plan.networkTypes);

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
