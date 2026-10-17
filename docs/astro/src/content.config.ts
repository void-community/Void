import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { remark } from 'remark';
import { visit } from 'unist-util-visit';

import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';
import { defineCollection } from 'astro:content';
import type { LoaderContext } from 'astro/loaders';

const starlightVirtualCompositeLoader = () => {
    var nativeStarlightLoader = docsLoader();
    var rawDirectoryPath = path.resolve(process.cwd(), 'src', 'reference');

    function splitDocumentTitle(documentTitle: string) {
        var trimmedTitle = documentTitle.trim();
        var firstSpaceIndex = trimmedTitle.indexOf(' ');

        if (firstSpaceIndex < 0) {
            return {
                groupLabel: trimmedTitle,
                itemLabel: trimmedTitle
            };
        }

        return {
            groupLabel: trimmedTitle.slice(0, firstSpaceIndex),
            itemLabel: trimmedTitle.slice(firstSpaceIndex + 1).trim()
        };
    }

    function getVisibleTextFromMarkdownNode(markdownNode: any): string {
        if (markdownNode == null)
            return '';

        if (markdownNode.type === 'text')
            return markdownNode.value;

        if (!Array.isArray(markdownNode.children))
            return '';

        var textParts: string[] = [];

        for (var childNode of markdownNode.children)
            textParts.push(getVisibleTextFromMarkdownNode(childNode));

        return textParts.join('');
    }

    function shouldSkipLinkUrl(linkUrl: string) {
        return linkUrl.startsWith('#')
            || linkUrl.startsWith('/')
            || linkUrl.startsWith('http://')
            || linkUrl.startsWith('https://')
            || linkUrl.startsWith('mailto:');
    }

    function splitLinkUrl(linkUrl: string) {
        var hashIndex = linkUrl.indexOf('#');
        var queryIndex = linkUrl.indexOf('?');
        var suffixStartIndex = -1;

        if (hashIndex >= 0 && queryIndex >= 0)
            suffixStartIndex = Math.min(hashIndex, queryIndex);
        else if (hashIndex >= 0)
            suffixStartIndex = hashIndex;
        else if (queryIndex >= 0)
            suffixStartIndex = queryIndex;

        if (suffixStartIndex < 0) {
            return {
                linkPath: linkUrl,
                linkSuffix: ''
            };
        }

        return {
            linkPath: linkUrl.slice(0, suffixStartIndex),
            linkSuffix: linkUrl.slice(suffixStartIndex)
        };
    }

    function stripMarkdownExtension(filePath: string) {
        if (filePath.endsWith('.md'))
            return filePath.slice(0, -3);

        return filePath;
    }

    function transformMarkdownDocument(markdownContent: string, currentDocumentRelativePath: string) {
        var syntaxTree = remark().parse(markdownContent);
        var extractedDocumentTitle: string | null = null;
        var currentDocumentDirectoryPath = path.posix.dirname(currentDocumentRelativePath);

        visit(syntaxTree, 'link', function (linkNode: any) {
            if (typeof linkNode.url !== 'string')
                return;

            if (shouldSkipLinkUrl(linkNode.url))
                return;

            var splitLink = splitLinkUrl(linkNode.url);
            var normalizedLinkPath = stripMarkdownExtension(splitLink.linkPath);
            var resolvedReferencePath = path.posix.normalize(path.posix.join(currentDocumentDirectoryPath, normalizedLinkPath));

            if (resolvedReferencePath.startsWith('../'))
                return;

            linkNode.url = '/reference/' + resolvedReferencePath + splitLink.linkSuffix;
        });

        if (Array.isArray((syntaxTree as any).children) && (syntaxTree as any).children.length > 0) {
            var firstNode = (syntaxTree as any).children[0];

            if (firstNode.type === 'heading' && firstNode.depth === 1) {
                extractedDocumentTitle = getVisibleTextFromMarkdownNode(firstNode).trim();
                (syntaxTree as any).children.shift();
            }
        }

        return {
            title: extractedDocumentTitle,
            body: String(remark().stringify(syntaxTree))
        };
    }

    return {
        name: 'starlight-virtual-composite-loader',
        load: async function (context: LoaderContext) {
            await nativeStarlightLoader.load(context);

            var astroStore = context.store;
            var astroParseData = context.parseData;
            var astroLogger = context.logger;

            async function processMarkdownFile(fullFilePath: string, fileName: string) {
                var relativeFilePath = path.relative(rawDirectoryPath, fullFilePath);
                var posixRelativePath = relativeFilePath.split(path.sep).join('/');

                var rawFileContents = await fs.readFile(fullFilePath, 'utf-8');
                var transformedDocument = transformMarkdownDocument(rawFileContents, posixRelativePath);
                var generatedDocumentTitle = transformedDocument.title || stripMarkdownExtension(fileName);

                var documentId = 'reference/' + stripMarkdownExtension(posixRelativePath);
                var splitTitle = splitDocumentTitle(generatedDocumentTitle);

                var parsedFrontmatterData = await astroParseData({
                    id: documentId,
                    data: {
                        title: generatedDocumentTitle,
                        slug: documentId,
                        sidebar: {
                            label: splitTitle.itemLabel
                        }
                    }
                });

                var spoofedFilePath = path.posix.join('src', 'content', 'docs', 'reference', splitTitle.groupLabel, posixRelativePath);
                var markdownForRendering = `---\nslug: ${documentId}\n---\n\n${transformedDocument.body}`;
                var renderedDocument = await context.renderMarkdown(markdownForRendering, {
                    fileURL: pathToFileURL(fullFilePath)
                });

                astroStore.set({
                    id: documentId,
                    data: parsedFrontmatterData,
                    body: transformedDocument.body,
                    rendered: renderedDocument,
                    filePath: spoofedFilePath
                });
            }

            async function processVirtualDirectory(currentSourceDirectory: string) {
                try {
                    var directoryEntries = await fs.readdir(currentSourceDirectory, { withFileTypes: true });

                    for (var fileEntry of directoryEntries) {
                        var fullFilePath = path.join(currentSourceDirectory, fileEntry.name);

                        if (fileEntry.isDirectory()) {
                            await processVirtualDirectory(fullFilePath);
                            continue;
                        }

                        if (fileEntry.isFile() && fileEntry.name.endsWith('.md'))
                            await processMarkdownFile(fullFilePath, fileEntry.name);
                    }
                } catch (directoryError: any) {
                    if (directoryError != null && directoryError.code !== 'ENOENT')
                        astroLogger.error(`[virtual-loader] Failed to read directory: ${directoryError.message}`);
                }
            }

            await processVirtualDirectory(rawDirectoryPath);
            astroLogger.info('Successfully injected virtual reference documents alongside standard Starlight docs.');
        }
    };
};

const repositoryApiUrl = 'https://api.github.com/repos/void-community/Void';
const artifactRequestBatchSize = 8;

const getHeaders = (): Record<string, string> => {
    const headers: Record<string, string> = {};
    const token = process.env.GITHUB_TOKEN;

    if (token)
        headers.Authorization = `Bearer ${token}`;

    return headers;
};

const fetchJson = async (url: string, headers: Record<string, string>): Promise<any> => {
    const response = await fetch(url, { headers });

    if (!response.ok)
        throw new Error(`GitHub API ${response.status} ${response.statusText} for ${url}`);

    return await response.json();
};

export const collections = {
    docs: defineCollection({
        loader: starlightVirtualCompositeLoader(),
        schema: docsSchema()
    }),
    releases: defineCollection({
        loader: async () => {
            const data = await fetchJson(`${repositoryApiUrl}/releases`, getHeaders());

            if (!Array.isArray(data))
                throw new Error(`Unexpected response from GitHub Releases API: ${JSON.stringify(data)}`);

            return data.map((item: any) => ({ ...item, id: item.id.toString() }));
        }
    }),
    workflows: defineCollection({
        loader: async () => {
            const headers = getHeaders();

            const [workflowsData, runsData] = await Promise.all([
                fetchJson(`${repositoryApiUrl}/actions/workflows?per_page=100`, headers),
                fetchJson(`${repositoryApiUrl}/actions/runs?per_page=100`, headers)
            ]);

            if (!Array.isArray(workflowsData.workflows))
                throw new Error(`Unexpected response from GitHub Workflows API: ${JSON.stringify(workflowsData)}`);

            if (!Array.isArray(runsData.workflow_runs))
                throw new Error(`Unexpected response from GitHub Workflow Runs API: ${JSON.stringify(runsData)}`);

            // The repository-wide artifacts endpoint times out for this repository, so artifacts are requested per successful run.
            const successfulRuns = runsData.workflow_runs.filter((run: any) => run.conclusion === 'success');
            const artifactsByRunId = new Map<string, any[]>();

            for (let batchStart = 0; batchStart < successfulRuns.length; batchStart += artifactRequestBatchSize) {
                const batchRuns = successfulRuns.slice(batchStart, batchStart + artifactRequestBatchSize);
                const batchArtifactsData = await Promise.all(
                    batchRuns.map((run: any) => fetchJson(`${repositoryApiUrl}/actions/runs/${run.id}/artifacts?per_page=100`, headers))
                );

                batchRuns.forEach((run: any, batchIndex: number) => {
                    const artifactsData = batchArtifactsData[batchIndex];

                    if (!Array.isArray(artifactsData.artifacts))
                        throw new Error(`Unexpected response from GitHub Artifacts API: ${JSON.stringify(artifactsData)}`);

                    artifactsByRunId.set(
                        run.id.toString(),
                        artifactsData.artifacts.map((artifact: any) => ({ ...artifact, id: artifact.id.toString() }))
                    );
                });
            }

            const runsByWorkflowId = new Map<string, any[]>();

            for (const run of runsData.workflow_runs) {
                const workflowId = run.workflow_id?.toString();

                if (!workflowId)
                    continue;

                const currentRuns = runsByWorkflowId.get(workflowId) ?? [];

                currentRuns.push({
                    ...run,
                    id: run.id.toString(),
                    artifacts: artifactsByRunId.get(run.id.toString()) ?? []
                });

                runsByWorkflowId.set(workflowId, currentRuns);
            }


            const results = workflowsData.workflows.map((workflow: any) => ({
                ...workflow,
                id: workflow.id.toString(),
                runs: runsByWorkflowId.get(workflow.id.toString()) ?? []
            }));

            return results;
        }
    })
};
